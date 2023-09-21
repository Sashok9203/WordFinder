using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WordFinder.Models
{
    public enum Status
    {
        Idle,
        Scaning,
        Ready,
        Searching,
        Cancellation
    }

    internal class WindowModel :INotifyPropertyChanged
    {
        private Status status = Status.Idle;
        private string? path;
        private string? word;
        private int progress;
        private double progressTick;
        private double progressValue;
        private AutoResetEvent progressEvent = new(true);
        private CancellationTokenSource? tokenSource;
        private CancellationToken token;

        private List<string> files = new();
       
        private void openDirectory()
        {
            System.Windows.Forms.FolderBrowserDialog fbd = new();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DirectoryPath = fbd.SelectedPath;
                CurentStatus = Status.Idle;
                clearInfo();
            }
        }

        private void clearInfo()
        {
            FileInfos.Clear();
            Progress = 0;
            progressValue = 0;
        }

        private async void scanFind()
        {
            tokenSource?.Dispose();
            tokenSource = new();
            token = tokenSource.Token;
            if (CurentStatus == Status.Idle)
            {
                CurentStatus = Status.Scaning;
                await Task.Run(() =>
                {
                   files.Clear();
                   var scanedfiles =  Directory.EnumerateFiles(DirectoryPath, "*.txt",
                                         new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true });
                    foreach (var file in scanedfiles) 
                    {
                        if (token.IsCancellationRequested) return;
                        files.Add(file);
                    }
                }, token);
                if (token.IsCancellationRequested)
                {
                    CurentStatus = Status.Idle;
                    return;
                }
                else if (files.Count > 0)
                        progressTick = (double)100 / files.Count;
                else
                {
                    CurentStatus = Status.Idle;
                    MessageBox.Show("No files found in this directory", "Message");
                    return;
                }
            }

            clearInfo();
           
            CurentStatus = Status.Searching;
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            };
            try
            {
                await Parallel.ForEachAsync(files, parallelOptions, async (file, tk) =>
                {
                    if (tk.IsCancellationRequested) return;
                    await Task.Run(async () =>
                    {
                        string? text;
                        try { text = await File.ReadAllTextAsync(file, tk); } catch { return; }
                        int wordCount = text?.Split(new char[] { '.', '?', '!', ' ', ';', ':', ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                                        .AsParallel().WithCancellation(tk).Where(x => x == Word).Count() ?? 0;
                        if (tk.IsCancellationRequested) return;
                        if (wordCount > 0)
                        {
                            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                FileInfos.Add(new()
                                {
                                    FileName = Path.GetFileName(file),
                                    FilePath = Path.GetDirectoryName(file),
                                    Count = wordCount
                                });
                            }));
                        }
                    }, tk);

                    progressEvent.WaitOne();
                    progressValue += progressTick;
                    progressEvent.Set();
                    int temp = (int)Math.Round(progressValue, 1);
                    if (temp > Progress)
                        await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { Progress = temp; }));
                });
            }
            catch 
            {

            }
            CurentStatus = Status.Ready;
            if (FileInfos.Count == 0) MessageBox.Show("Word not found in this files","Message");
            
        }

        private void exitStop()
        {
            if (CurentStatus == Status.Idle || CurentStatus == Status.Ready) Application.Current.Shutdown();
            else
            {
                tokenSource?.Cancel();
                CurentStatus = Status.Cancellation;
            }
        }

        public string? DirectoryPath
        {
            get => path;
            set 
            {
                path = value;
                OnPropertyChanged();
               // OnPropertyChanged(nameof(WordTextBoxEnabled));
            }
        }

        public string? Word
        {
            get => word;
            set
            {
                word = value;
                OnPropertyChanged();
            }
        }

        public string? ProgressStr => progress == 0 ? null : $"{progress} %";
        
        public int Progress
        {
            get => progress;
            set
            {
                progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressStr));
            }
        }

        public Status CurentStatus 
        { 
            get => status;
            set
            {
                status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressBarIndeterminate));
                //OnPropertyChanged(nameof(WordTextBoxEnabled));
                OnPropertyChanged(nameof(ExitStopButtonName));
                OnPropertyChanged(nameof(DisplayStatus));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string DisplayStatus
        {
            get
            {
                string result = string.Empty;
                switch (CurentStatus)
                {
                    case Status.Cancellation:
                        result = "Cancellation process...";
                        break;
                    case Status.Scaning:
                        result = "Scaning directory...";
                        break;
                    case Status.Searching:
                        result = "Word searching...";
                        break;
                    case Status.Ready:
                        result = $"Ready to word searching ... {files.Count} files were found in the directory to search";
                        break;
                    case Status.Idle:
                        result = "Ready to directory scaning...";
                        break;
                }
                return result;
            }
        }

       // public bool WordTextBoxEnabled => Path.Exists(DirectoryPath) && (CurentStatus == Status.Ready || CurentStatus == Status.Idle);

        public bool ProgressBarIndeterminate => CurentStatus == Status.Scaning;

        public string? ExitStopButtonName => CurentStatus == Status.Idle || CurentStatus == Status.Ready ? "Exit" : CurentStatus == Status.Cancellation ? "Cancellation ..." : "Stop";

        public ObservableCollection<FileInfo> FileInfos { get; set; } = new();

        public RelayCommand OpenDirectory => new((o)=>openDirectory());
        public RelayCommand ScanFindButton => new((o) => scanFind() ,(o) => (CurentStatus == Status.Ready || CurentStatus == Status.Idle) && Path.Exists(DirectoryPath) && !string.IsNullOrEmpty(Word));
        public RelayCommand ExitStopButton => new((o) => exitStop(),(o) => CurentStatus!=Status.Cancellation);
        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new(prop));
    }
}
