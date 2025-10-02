using System;
using System.Windows;

namespace SteamDownloader
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            app.Run(new MainWindow());
        }
    }
}