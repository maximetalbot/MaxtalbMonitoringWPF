using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MonitoringWPF
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /*
         * Aiguille: 
         * rotation min -77 = 0%
         * rotation max 165 = 100%
         * total mouvement = 242°
         * soit 2.42° par %d'utilisation
         * 
         */
        public MainWindow()
        {
            InitializeComponent();
            // Récupération des informations de l'ordinateur.
            GetAllSystemInfos();
            // Récup info disques
            GetDrivesInfos();

            // Timer pour la mise à jour semi temps réel.
            DispatcherTimer timer  = new DispatcherTimer();
            // On lui précise un interval d'une demie seconde.
            timer.Interval = TimeSpan.FromSeconds(0.75);
            // le tick est le moment où le chrono atteint l'interval
            timer.Tick += timer_Tick;
            // Démarrer le timer
            timer.Start();
        }

        // Fonction timer qui refresh l'écran presque 2 fois par sec.
        public void timer_Tick(object sender, EventArgs e)
        {
            // màj infos CPU
            cpu.Content = RefreshCpuInfos();
            // màj infos RAM
            RefreshRamInfos();
            // màj infos température
            RefreshTempInfos();
            // màj infos réseau
            RefreshNetworkInfos();
        }

        // Accès aux informations des disques
        public void GetDrivesInfos()
        {
            DriveInfo[] allDrives = DriveInfo.GetDrives();
            List<Disque> disques = new List<Disque>();
            foreach (DriveInfo info in allDrives)
            {
                if (info.IsReady == true)
                {
                    Console.WriteLine("Disque {0} prêt.", info.Name);
                    Console.WriteLine("  File type: {0}", info.DriveType);
                    disques.Add(new Disque(info.Name, info.DriveFormat, FormatSize(info.TotalSize), FormatSize(info.AvailableFreeSpace)));
                }                
            }
            listeDisques.ItemsSource = disques;
        }

        // Fonction de formatage des bytes récupérées
        private static string FormatSize(long bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }
            // On retourne une nouvelle string avec les valeurs voulues
            return String.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        public void RefreshNetworkInfos()
        {
            // Interface pour accéder aux informations réseau
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                // Pas d'accès au réseau, donc sortie de la fonction
                return;
            }
            // Dans un tableau on stock toute les interfaces réseau
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach(NetworkInterface ni in interfaces)
            {
                IPv4InterfaceStatistics interfaceStats = ni.GetIPv4Statistics();
                
                // Données cumulées:
                // Pour chaque interface réseau on va calculer le débit, si plus grand que 0
                if (interfaceStats.BytesSent > 0)
                {
                    //var upOld = netMont.Content;
                    //if (!netMont.Content.Equals(typeof(long)))
                    //{
                    //    upOld = Convert.ToInt64(netDesc.Content);
                    //}
                    var upNew = interfaceStats.BytesSent / 1000;                    
                    netMont.Content = upNew;
                    //if (interfaceStats.BytesSent == 0)
                    //{
                    //    upOld = 0;
                    //}
                    //debitMont.Content = upNew - (long)upOld + " kB/s";
                }
                if (interfaceStats.BytesReceived > 0)
                {
                    var dlOld = netDesc.Content;
                    if (!netDesc.Content.Equals(typeof(long)))
                    {
                        dlOld = Convert.ToInt64(netDesc.Content);
                    }
                    var dlNew = interfaceStats.BytesReceived / 1000;
                    // Je dois retirer les unités car cela en ferait un objet {string} non castable en long
                    netDesc.Content = dlNew;
                    debitDesc.Content = dlNew - (long)dlOld + " kB/s";
                }
            }
        }
        public void RefreshTempInfos()
        {
            Double temperature = 0;
            String instanceName = "";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
             //Ne fonctionne pas avec ma carte - mère, à revoir chez moi
            try
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    temperature = Convert.ToDouble(obj["CurrentTemperature"].ToString());
                    // Convertir les °F en °C
                    temperature = (temperature - 2732) / 10.0;
                    instanceName = obj["InstanceName"].ToString();
                }
                temp.Content = temperature + "°C";
            }
            catch (ManagementException err)
            {
                temp.Content = "err. " + err.Message;
            }
        }
        public void RefreshRamInfos()
        {
            ramTotal.Content = "RAM totale : " + FormatSize(GetTotalPhys());
            ramUsed.Content = "RAM utilisée : " + FormatSize(GetUsedPhys());
            ramFree.Content = "RAM disponible: " + FormatSize(GetAvailPhys());

            // On récupère l'info 11.11GB qu'on découpe pour isoler ce qu'il nous faut
            string[] maxVal = FormatSize(GetTotalPhys()).Split(' ');
            string[] memVal = FormatSize(GetUsedPhys()).Split(' ');

            barRam.Maximum = float.Parse(maxVal[0]);
            barRam.Value = float.Parse(memVal[0]);
        }
        // Pour appeller toute les fonctions du timer pour rafraichir l'écran
        public string RefreshCpuInfos()
        {
            PerformanceCounter cpuCounter = new PerformanceCounter();
            cpuCounter.CategoryName = "Processor";
            cpuCounter.CounterName = "% Processor Time";
            cpuCounter.InstanceName = "_Total";
            
            // Récupération de l'information 
            dynamic firstVal = cpuCounter.NextValue();          // cette valeur sera toujours 0
            System.Threading.Thread.Sleep(75);  // permet de faire patienter le thread
            dynamic val = cpuCounter.NextValue();               // ici on récupère la vraie valeur

            // Tourner l'image de l'aiguille, cf calcul en haut 
            RotateTransform rotateTransform = new RotateTransform((val*2.42f) - 77);
            imgAiguille.RenderTransform = rotateTransform;

            // Convertion de la valeur récupérée en décimal, avec précision de 2
            decimal roundVal = (decimal)val;
            roundVal = Math.Round(roundVal,2);

            // En retour, la valeur sous forme de chaine
            return roundVal + " %";
        }
        #region Fonctions sépcifiques à la RAM
        // On importe la DLL kernel32
        [DllImport("kernel32.dll")]
        // L'utilisation du Marshal sert à faciliter le travaille sur des dll, peu importe le langage de programation employé
        [return: MarshalAs(UnmanagedType.Bool)]
        // On peut donc accéder à une fonction externe (celle du dll) en l'occurence celle de la mémoire
        public static extern bool GlobalMemoryStatusEx(ref MEMORY_INFO mi);

        // Structure de l'info de la mémoire
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_INFO
        {
            public uint dwLength;           // Taille structure
            public uint dwMemoryLoad;       // Utilisation mémoire
            public ulong ullTotalPhys;      // Mémoire physique totale
            public ulong ullAvailPhys;      // Mémoire physique disponible
            public ulong ullTotalPageFiles;
            public ulong ullAvailPageFiles;
            public ulong ullTotalVirtual;   // Mémoire virtuelle totale
            public ulong ullAvailVirtual;   // Mémoire virtuelle disponible
            public ulong ullAvailExtendedVirtual;
        }

        // Il faut ensuite formater les valeurs récupérées pour pouvoir les comprendre
        // en paramètre on récupère le retour de la fonction gérant les informations mémoire
        static string FormatSize(double size)
        {
            double d = (double)size;
            int i = 0;
            // 1024 est le multiplicateur, 5 représente le nombre d'unités possibles
            while((d > 1024) && (i < 5))
            {
                d /= 1024;
                i++;
            }
            string[] unit = { "B", "KB", "MB", "GB", "TB" };
            // On retourne une nouvelle string avec les valeurs voulues
            return (string.Format("{0} {1}", Math.Round(d, 2), unit[i]));
        }
        // Accès au status de la mémoire
        public static MEMORY_INFO GetMemoryStatus()
        {
            MEMORY_INFO mi = new MEMORY_INFO();
            mi.dwLength = (uint)Marshal.SizeOf(mi);
            GlobalMemoryStatusEx(ref mi);
            return mi;
        }

        // Récupération mémoire physique totale disponible
        public static ulong GetAvailPhys()
        {
            MEMORY_INFO mi = GetMemoryStatus();
            return mi.ullAvailPhys;
        }

        // Récupération mémoire utilisée
        public static ulong GetUsedPhys()
        {
            MEMORY_INFO mi = GetMemoryStatus();
            return (mi.ullTotalPhys - mi.ullAvailPhys);
        }

        // Récupération mémoire physique totale
        public static ulong GetTotalPhys()
        {
            MEMORY_INFO mi = GetMemoryStatus();
            return mi.ullTotalPhys;
        }
        #endregion

        public void GetAllSystemInfos()
        {
            SystemInfo si = new SystemInfo();
            osName.Content = si.GetOsInfos("os");
            osArch.Content = si.GetOsInfos("architecture");
            procName.Content = si.GetCpuInfos();
            gpuName.Content = si.GetGpuInfos();
        }
        private void infoMsg_MouseDown(object sender, MouseButtonEventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/maxtalb");
        }
    }

    /// <summary>
    /// Récupération des informations du système d'exploitation.
    /// </summary> 
    public class SystemInfo
    {
        public string GetOsInfos(string param)
        {
            // On créer un nouvel objet qui récupère toute les informations du système d'exploitation
            ManagementObjectSearcher mos = new ManagementObjectSearcher("select * from Win32_OperatingSystem");
            // Nous parcourons le tableau de résultats pour en selectionner ce que nous souhaitons dans "param"
            foreach(var mo in mos.Get())
            {
                // Selection des cas susceptibles de nous interesser
                switch (param)
                {
                    case "os":
                        return (string)mo["Caption"];
                    case "architecture":
                        return (string)mo["OSArchitecture"];
                    case "osVersion":
                        return (string)mo["CSDVersion"];
                }
            }
            return null;
        }

        /// <summary>
        /// Récupération des informations du CPU
        /// </summary>
        public string GetCpuInfos()
        {
            // On pourra contrôler cette information manuellement via léditeur de registre.
            // Récupération des informations du processeur en allant chercher dans le registre au chemin spécifié. Le second paramètre sert à lire l'arbre.
            RegistryKey processor_name = Registry.LocalMachine.OpenSubKey(@"Hardware\Description\System\CentralProcessor\0", RegistryKeyPermissionCheck.ReadSubTree);

            if (processor_name != null)
            {
                // On ne retourne que la valeure qui correspond à la clé souhaitée, castée au type attendu.
                return (string)processor_name.GetValue("ProcessorNameString");
            }
            return null;
        }

        /// <summary>
        /// Récupération des informations du GPU
        /// </summary>
        public string GetGpuInfos()
        {
            using (var searcher = new ManagementObjectSearcher("select * from Win32_VideoController"))
            {
                foreach (var obj in searcher.Get())
                {
                    Console.WriteLine("Name - "+obj["Name"]);
                    Console.WriteLine("DeviceID - "+obj["DeviceID"]);
                    Console.WriteLine("AdapterRAM - "+obj["AdapterRAM"]);
                    Console.WriteLine("AdapterDACType - "+obj["AdapterDACType"]);
                    Console.WriteLine("Monochrome - "+obj["Monochrome"]);
                    Console.WriteLine("InstalledDisplayDrivers - "+obj["InstalledDisplayDrivers"]);
                    Console.WriteLine("DriverVersion - "+obj["DriverVersion"]);
                    Console.WriteLine("VideoProcessor - "+obj["VideoProcessor"]);
                    Console.WriteLine("VideoArchitecture - "+obj["VideoArchitecture"]);
                    Console.WriteLine("VideoMemoryType - "+obj["VideoMemoryType"]);

                    return (string)obj["Name"] + " (Pilote v. : " + (string)obj["DriverVersion"] + ")";
                }
            }
            //// Autre possibilité
            //ManagementObjectSearcher mos = new ManagementObjectSearcher("select * from Win32_VideoController ");
            //string video_controller = String.Empty;
            //foreach (var mo in mos.Get())
            //{
            //    if (mo["CurrentBitsPerPixel"] != null && mo["CurrentHorizontalResolution"] != null)
            //    {
            //        if ((String)mo["DeviceID"] == "VideoController1")
            //        {
            //            video_controller = (string)mo["Description"];
            //            return video_controller;
            //        }
            //    }
            //}
            return null;
        }

    }

    /// <summary>
    /// Infos pour les disques
    /// </summary>
    /// 
    public class Disque
    {
        private string name;
        private string format;
        private string totalSpace;
        private string freeSpace;
        // Constructeur
        public Disque(string n, string f,string t,string l)
        {
            name = n;
            format = f;
            totalSpace = t;
            freeSpace = l;
        }
        public override string ToString()
        {
            return name + " (" + format + ") " + freeSpace + " libres / " + totalSpace;
        }

    }
}