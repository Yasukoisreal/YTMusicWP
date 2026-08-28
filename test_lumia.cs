using System;
using System.Reflection;
using System.Linq;

namespace CheckLumia {
    class Program {
        static void Main() {
            var assembly = Assembly.LoadFrom(@"D:\Documents\Visual Studio 2015\Projects\YTMusicWP\packages\LumiaImagingSDK.2.0.208\lib\wpa81\x86\Lumia.Imaging.Managed.dll");
            Console.WriteLine("Managed loaded");
        }
    }
}
