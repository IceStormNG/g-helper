using GHelper.Display;
using GHelper.Mode;
using Microsoft.Win32;
using System.Management;
using System.Diagnostics;

namespace GHelper.Helpers
{
    internal class ClamshellModeControl
    {
        private int wmiMonitorCount = 0;
        private int externalMonitorCount = 0;
        private int internalMonitorCount = 0;


        public bool IsExternalDisplayConnected()
        {
            var devices = ScreenInterrogatory.GetAllDevices().ToArray();

            string internalName = AppConfig.GetString("internal_display");
            externalMonitorCount = 0;
            foreach (var device in devices)
            {
                if (device.outputTechnology != ScreenInterrogatory.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL &&
                    device.outputTechnology != ScreenInterrogatory.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
                    && device.monitorFriendlyDeviceName != internalName)
                {
                    Logger.WriteLine("ClamshellModeControl: Found external screen: " + device.monitorFriendlyDeviceName + ":" + device.outputTechnology.ToString());
                    ++externalMonitorCount;
                }

            }

            return externalMonitorCount > 0;
        }


        public void GetAllMonitorsWMI()
        {
            wmiMonitorCount = 0;
            try
            {
                ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT * FROM Win32_DesktopMonitor");

                foreach (ManagementObject queryObj in searcher.Get())
                {
                    Logger.WriteLine("ClamshellModeControl: Found WMI Monitor: " + queryObj["PNPDeviceID"].ToString());
                    if (queryObj["PNPDeviceID"].ToString() is not null && queryObj["PNPDeviceID"].ToString() != String.Empty)
                    {
                        ++wmiMonitorCount;
                    }
                }
            }
            catch (ManagementException e)
            {
                Logger.WriteLine(e.Message);
            }
        }

        public bool IsInternalDisplayConnected()
        {
            var devices = ScreenInterrogatory.GetAllDevices().ToArray();
            internalMonitorCount = 0;
            foreach (var device in devices)
            {
                if (device.outputTechnology == ScreenInterrogatory.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL ||
                    device.outputTechnology == ScreenInterrogatory.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED)
                {
                    Logger.WriteLine("ClamshellModeControl: Found internal screen: " + device.monitorFriendlyDeviceName + ":" + device.outputTechnology.ToString());
                    ++internalMonitorCount;
                }

            }

            return internalMonitorCount > 0;
        }

        public bool IsLidClosed()
        {
            return !IsInternalDisplayConnected();
        }

        public void SubscribeToDisplayConnectionEvents()
        {
            try
            {
                ManagementEventWatcher watcher = new ManagementEventWatcher();
                watcher.EventArrived += new EventArrivedEventHandler(EventArrived);
                watcher.Scope = new ManagementScope("root\\wmi");
                watcher.Query = new WqlEventQuery("SELECT * FROM AsusAtkWmiEvent WHERE EventID = 192");
                watcher.Start();
            }
            catch
            {
                Logger.WriteLine("ClamshellModeControl: Can't connect to ASUS WMI events");
            }
        }

        private void EventArrived(object sender, EventArrivedEventArgs e)
        {
            if (e.NewEvent is null) return;
            int EventID = int.Parse(e.NewEvent["EventID"].ToString());
            if (EventID == 192)
            {
                int oldCount = wmiMonitorCount;
                GetAllMonitorsWMI();
                int displayDelta = Math.Abs(oldCount - wmiMonitorCount);

                if (oldCount > wmiMonitorCount)
                {
                    Logger.WriteLine("ClamshellModeControl: Display disconnected. Displays removed: " + displayDelta);
                }
                else
                {
                    Logger.WriteLine("ClamshellModeControl: Display connected. New Displays: " + displayDelta);
                }


                bool externalDisplayLeft = true;

                if (oldCount < wmiMonitorCount && displayDelta >= externalMonitorCount)
                {
                    //All external screens got disconnected
                    externalDisplayLeft = false;
                }

                //Only call if there was an external screen connected, clamshall is engegaged, but all external screens are (likely) gone now
                if (IsExternalDisplayConnected() && IsClamshellEnabled() && !IsInternalDisplayConnected() && !externalDisplayLeft)
                {
                    //PowerNative.SetLidAction(1, true);
                    Process.Start("C:\\Windows\\System32\\DisplaySwitch.exe", "/internal");
                    Task.Delay(1000 * 1).ContinueWith(t => Program.acpi.DeviceSet(AsusACPI.UniversalControl, AsusACPI.KB_Sleep, "Sleep"));

                }
            }
        }


        public void PrintClamshellStatus()
        {
            Logger.WriteLine("ClamshellModeControl: " + IsClamshellEnabled() + ", Total Screens: " +
                wmiMonitorCount + ", External Screens: " +
                externalMonitorCount + ", Internal Screens: " +
                internalMonitorCount + ", Lid Closed: " + IsLidClosed());
        }


        public bool IsClamshellEnabled()
        {
            return AppConfig.Get("toggle_clamshell_mode") != 0;
        }

        public bool IsChargerConnected()
        {
            return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        }

        public bool IsInClamshellMode()
        {
            return IsExternalDisplayConnected() && IsChargerConnected();
        }

        public void ToggleLidAction()
        {
            PrintClamshellStatus();
            if (IsInClamshellMode() && IsClamshellEnabled())
            {
                PowerNative.SetLidAction(0, true);
                Logger.WriteLine("ClamshellModeControl: Engaging Clamshell Mode");
            }
            else
            {
                PowerNative.SetLidAction(1, true);
                Logger.WriteLine("ClamshellModeControl: Disengaging Clamshell Mode");
            }
        }

        public void UnregisterDisplayEvents()
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        }

        public void RegisterDisplayEvents()
        {
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            SubscribeToDisplayConnectionEvents();
            GetAllMonitorsWMI();
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Logger.WriteLine("ClamshellModeControl: Display configuration changed.");

            if (IsClamshellEnabled())
                ToggleLidAction();
        }
    }
}
