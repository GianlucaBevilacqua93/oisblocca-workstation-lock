using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace OISblocca.Core
{
    public enum SiteStatus
    {
        Checking,
        Online,
        Offline
    }

    public class SiteItem : INotifyPropertyChanged
    {
        private string _domain = string.Empty;
        private string _description = string.Empty;
        private bool _isEnabled = true;
        private SiteStatus _status = SiteStatus.Checking;
        private int _latencyMs = -1;
        private string _statusMessage = "Verifica in corso...";
        private List<string> _resolvedIps = new();

        public string Domain
        {
            get => _domain;
            set { _domain = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusOpacity));
                }
            }
        }

        [JsonIgnore]
        public SiteStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusGlowBrush));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        [JsonIgnore]
        public int LatencyMs
        {
            get => _latencyMs;
            set
            {
                _latencyMs = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LatencyDisplay));
            }
        }

        [JsonIgnore]
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public List<string> ResolvedIps
        {
            get => _resolvedIps;
            set
            {
                _resolvedIps = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IpCountDisplay));
            }
        }

        [JsonIgnore]
        public string IpCountDisplay => _status switch
        {
            SiteStatus.Online => "Pronto per la gara",
            SiteStatus.Offline => "Non raggiungibile",
            _ => "Verifica in corso"
        };

        [JsonIgnore]
        public string LatencyDisplay => _latencyMs >= 0 ? $"{_latencyMs} ms" : "--";

        [JsonIgnore]
        public string StatusText => _status switch
        {
            SiteStatus.Online => "Online",
            SiteStatus.Offline => "Non raggiungibile",
            _ => "Verifica..."
        };

        [JsonIgnore]
        public double StatusOpacity => _isEnabled ? 1.0 : 0.4;

        [JsonIgnore]
        public System.Windows.Media.Brush StatusBrush => _status switch
        {
            SiteStatus.Online => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118)),   // Green #00E676
            SiteStatus.Offline => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 82, 82)),  // Red #FF5252
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 179, 0))                   // Amber #FFB300
        };

        [JsonIgnore]
        public System.Windows.Media.Brush StatusGlowBrush => _status switch
        {
            SiteStatus.Online => new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 0, 230, 118)),
            SiteStatus.Offline => new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 82, 82)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 255, 179, 0))
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
