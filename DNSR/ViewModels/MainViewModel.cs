using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DNSR.Models;
using System.Diagnostics;
using DNSR.RNNoise;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DNSR.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private WaveIn _waveIn;
        private Denoiser _denoiser;
        private WaveOut _waveOut;
        private BufferedWaveProvider _playBuffer;
        private List<string> _outputDevices;
        private Configuration? _configuration;
        private RegistryKey startupKey = Registry.CurrentUser.OpenSubKey
            ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

        public ObservableCollection<string> InputDevices { get; set; }
        public Configuration Configuration => _configuration;
        private int _inputDevice;

        public int InputDevice
        {
            get { return _inputDevice; }
            set
            {
                if (SetProperty(ref _inputDevice, value))
                {
                    _configuration.InputDevice = value;
                    WriteConfig();
                    StopRecording();
                    StartRecording();
                }
            }
        }


        private string _log;
        public string Log
        {
            get => _log;
            set => App.Current.Dispatcher.Invoke(() => _log = value);
        }



        public MainViewModel()
        {
            Log = string.Empty;
            _outputDevices = new List<string>();
            InputDevices = new ObservableCollection<string>();

            LoadConfig();
            StartProcessing();
            AddToStartup();
        }

        private async Task AddToStartup()
        {
            await Task.Delay(1000);
            var dnsrKey = startupKey.GetValue("DNSR");
            if (dnsrKey == null)
            {
                startupKey.SetValue("DNSR", System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                (App.Current.MainWindow as MainWindow).RootSnackbar.Show("Notification", "Application is added to auto startup. You can disable it in Task Manager");
            }
        }

        private void LoadConfig()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DNSR");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            path = Path.Combine(path, "config.json");
            if (!File.Exists(path))
                File.WriteAllText(path, JsonSerializer.Serialize(new Configuration()));
            var configJson = File.ReadAllText(path);
            _configuration = JsonSerializer.Deserialize<Configuration>(configJson);
            _inputDevice = _configuration.InputDevice;
        }

        public void ExecuteAsAdmin(string fileName)
        {
            Process proc = new Process();
            proc.StartInfo.FileName = fileName;
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.Verb = "runas";
            proc.Start();
            proc.WaitForExit();
        }
        public void WriteConfig()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DNSR");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            path = Path.Combine(path, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(_configuration));
        }

        private async Task StartProcessing()
        {
            Out("Starting processing");
            GetAudioDevices();
            if (!StartPlayback())
                return;

            StartRecording();
            Out("Processing started. Enjoy your noise free audio (Don't forget to change input device in desired application to VB-Audio Virtual Cable ( ͡° ͜ʖ ͡°) )");
        }

        private void GetAudioDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            int waveOutDevices = WaveOut.DeviceCount;
            for (int waveOutDevice = 0; waveOutDevice < waveOutDevices; waveOutDevice++)
            {
                WaveOutCapabilities deviceInfo = WaveOut.GetCapabilities(waveOutDevice);
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
                {
                    try
                    {
                        if (device.FriendlyName.StartsWith(deviceInfo.ProductName))
                        {
                            _outputDevices.Add(device.FriendlyName);
                            break;
                        }
                    }
                    catch { }
                }
            }
            int waveInDevices = WaveIn.DeviceCount;
            for (int waveInDevice = 0; waveInDevice < waveInDevices; waveInDevice++)
            {
                WaveInCapabilities deviceInfo = WaveIn.GetCapabilities(waveInDevice);
                foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All))
                {
                    try
                    {
                        if (device.FriendlyName.StartsWith(deviceInfo.ProductName))
                        {
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                InputDevices.Add(device.FriendlyName);
                            });
                            break;
                        }
                    }
                    catch { }
                }
            }

        }

        private bool StartPlayback()
        {
            _waveOut = new WaveOut(WaveCallbackInfo.FunctionCallback());
            var device = _outputDevices.IndexOf(_outputDevices.FirstOrDefault(x => x.StartsWith("CABLE Input (VB-Audio Virtual Cable)")));
            if (device == -1)
            {
                ExecuteAsAdmin(Path.Combine(Environment.CurrentDirectory, "VBDriver", "VBCABLE_Setup_x64.exe"));
                MessageBox.Show("Please restart application");
                Environment.Exit(-1);
                return false;
            }
            _waveOut.DeviceNumber = device;
            _playBuffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(48000, 16, 2));
            _waveOut.Init(_playBuffer);
            _waveOut.Play();
            return true;
        }

        private void StartRecording()
        {
            _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback())
            {
                BufferMilliseconds = 40,
                DeviceNumber = Math.Clamp(_configuration!.InputDevice, 0, InputDevices.Count - 1),
            };
            _waveIn.DataAvailable += OnWaveInDataAvailable;
            _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(48000, 16, 2);
            _denoiser = new Denoiser();
            _waveIn.StartRecording();
        }

        private void StopRecording()
        {
            _waveIn?.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }

        private void OnWaveInDataAvailable(object? sender, WaveInEventArgs e)
        {
            var floats = GetFloatsFromBytes(e.Buffer, e.BytesRecorded);
            var floatsSpan = floats.AsSpan(0, floats.Length);
            _denoiser.Denoise(floatsSpan);
            var bytes = GetBytesFromFloats(floatsSpan.ToArray(), floatsSpan.Length);
            _playBuffer.AddSamples(bytes, 0, bytes.Length);
        }

        private static float[] GetFloatsFromBytes(byte[] buffer, int length)
        {
            float[] floats = new float[length / 2];
            int floatIndex = 0;
            for (int index = 0; index < length; index += 2)
            {
                short sample = (short)((buffer[index + 1] << 8) |
                                        buffer[index + 0]);
                // to floating point
                floats[floatIndex] = sample / 32768f;
                floatIndex++;
            }
            return floats;
        }

        private static byte[] GetBytesFromFloats(float[] samples, int samplesCount)
        {
            var pcm = new byte[samplesCount * 2];
            int sampleIndex = 0,
                pcmIndex = 0;

            while (sampleIndex < samplesCount)
            {
                var outsample = (short)(samples[sampleIndex] * short.MaxValue);
                pcm[pcmIndex] = (byte)(outsample & 0xff);
                pcm[pcmIndex + 1] = (byte)((outsample >> 8) & 0xff);

                sampleIndex++;
                pcmIndex += 2;
            }

            return pcm;
        }

        public void Out(string a)
        {
            Log += a + "\n";
        }
    }
}
