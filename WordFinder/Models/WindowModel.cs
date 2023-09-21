using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

        private readonly char[] splitChars = { '.', '?', '!', ' ', ';', ':', ',', '\n', '\r', '\t', '"', '\'' };
       
        private void openDirectory()
        {
            System.Windows.Forms.FolderBrowserDialog fbd = new();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DirectoryPath = fbd.SelectedPath;
                CurrentStatus = Status.Idle;
                clearInfo();
            }
        }

        private void clearInfo()
        {
            FileInfos.Clear();
            Progress = 0;
            progressValue = 0;
        }

        private void saveResult()
        {
            SaveFileDialog sfd = new()
            {
                Filter = "TXT files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "Result.txt",
            };

            if (sfd.ShowDialog() == true)
            {
                using StreamWriter sw = new(sfd.FileName);
                sw.WriteLine($" Searching word  -  \"{Word}\"");
                foreach (var file in FileInfos)
                {
                    sw.WriteLine(new string('-', file.FilePath.Length + 14));
                    sw.WriteLine($" File name  : {file.FileName}");
                    sw.WriteLine($" File path  : {file.FilePath}");
                    sw.WriteLine($" Word count : {file.Count}");
                }
            }
        }

        private async void findWordAsync()
        {
            tokenSource?.Dispose();
            tokenSource = new();
            token = tokenSource.Token;
            if (CurrentStatus == Status.Idle)
            {
                CurrentStatus = Status.Scaning;
                await Task.Run(() =>
                {
                    files.Clear();
                    var scanedfiles =  Directory.EnumerateFiles(DirectoryPath, "*.txt",new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true });
                    foreach (var file in scanedfiles) 
                    {
                        if (token.IsCancellationRequested) return;
                        files.Add(file);
                    }
                }, token);

                if (token.IsCancellationRequested)
                {
                    CurrentStatus = Status.Idle;
                    return;
                }
                else if (files.Count > 0)
                        progressTick = (double)100 / files.Count;
                else
                {
                    CurrentStatus = Status.Idle;
                    MessageBox.Show("No files found in this directory", "Message");
                    return;
                }
            }

            clearInfo();
           
            CurrentStatus = Status.Searching;
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
                        string text = string.Empty;
                        try { text = await File.ReadAllTextAsync(file, tk); } catch { return; }
                        int wordCount = text.Split(splitChars, StringSplitOptions.RemoveEmptyEntries)
                                             .AsParallel().WithCancellation(tk).Where(x => x == Word).Count();
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
            catch {}
            CurrentStatus = Status.Ready;
            if (FileInfos.Count == 0) MessageBox.Show("Word not found in these files", "Message");
        }

        private void showFile(object o)
        {
            FileInfo fi = o as FileInfo;
            Process.Start("notepad.exe",Path.Combine(fi.FilePath,fi.FileName));
        }

        private void exitStop()
        {
            if (CurrentStatus == Status.Idle || CurrentStatus == Status.Ready) Application.Current.Shutdown();
            else
            {
                tokenSource?.Cancel();
                CurrentStatus = Status.Cancellation;
            }
        }

        public string? DirectoryPath
        {
            get => path;
            set 
            {
                path = value;
                OnPropertyChanged();
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

        public Status CurrentStatus 
        { 
            get => status;
            set
            {
                status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressBarIndeterminate));
                OnPropertyChanged(nameof(ExitStopButtonName));
                OnPropertyChanged(nameof(DisplayStatus));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string DisplayStatus => CurrentStatus switch
        {
            Status.Cancellation => "Cancellation process...",
            Status.Scaning => "Scaning directory...",
            Status.Searching => "Word searching...",
            Status.Ready => $"Ready to word searching ... {files.Count} files were found in the directory to search",
            Status.Idle => "Ready to directory scaning...",
            _ => throw new NotImplementedException()
        };

        public string? ProgressStr => progress == 0 ? null : $"{progress} %";

        public bool ProgressBarIndeterminate => CurrentStatus == Status.Scaning;

        public string? ExitStopButtonName => CurrentStatus == Status.Idle || CurrentStatus == Status.Ready ? "Exit" : CurrentStatus == Status.Cancellation ? "Cancellation ..." : "Stop";

        public ObservableCollection<FileInfo> FileInfos { get; set; } = new();

        public RelayCommand OpenDirectory => new((o)=>openDirectory() ,(o) => CurrentStatus == Status.Ready || CurrentStatus == Status.Idle);

        public RelayCommand ScanFindButton => new((o) => findWordAsync() ,(o) => (CurrentStatus == Status.Ready || CurrentStatus == Status.Idle) && Path.Exists(DirectoryPath) && !string.IsNullOrEmpty(Word));

        public RelayCommand ExitStopButton => new((o) => exitStop(),(o) => CurrentStatus != Status.Cancellation);

        public RelayCommand SaveResultButton => new((o) => saveResult(), (o) => FileInfos.Count > 0);

        public RelayCommand DoubleClickCommand => new((o) => showFile(o));

        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string? prop = null) => PropertyChanged?.Invoke(this, new(prop));
    }
}
