using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;

namespace CloudFlareDNSClient
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        private CloudFlareData cfData;
        private string Now = "";
        private string ipInfoURL = "https://api.ipify.org";
        private string ip6InfoURL = "https://api64.ipify.org";
        private string recordIPv4 = "127.0.0.1";
        private string recordIPv6 = "[::1]";
        private string MODE_IPV4 = "4";
        private string MODE_IPV6 = "6";

        private string recordFileName = "tick.tmp";
        //private bool CloseAllowed;
        private Timer Timer_updateDNS;
        private System.Windows.Forms.NotifyIcon m_notifyIcon;
        private System.Windows.Controls.ContextMenu mContextMenu;
        public MainWindow()
        {
            InitializeComponent();
            cfData = new CloudFlareData();

            Timer_updateDNS = new System.Timers.Timer();
            Timer_updateDNS.Interval = 5000;
            Timer_updateDNS.AutoReset = true;
            Timer_updateDNS.Enabled = true;
            Timer_updateDNS.Stop();
            Timer_updateDNS.Elapsed += OnTimedEvent_UpdateDomain;

            InitTrayIcon();
            LoadRecord();

        }


        private void StartTimer()
        {
            Timer_updateDNS.Stop();
            Timer_updateDNS.Interval = Int32.Parse(cfData.updateInterval) * 60000;
            Timer_updateDNS.Start();

            Button_UpdateStart.IsEnabled = false;
            CheckBox_IPv6Enable.IsEnabled = false;
            Button_UpdateStop.IsEnabled = true;
        }

        private void StopTimer()
        {
            Timer_updateDNS.Stop();
            Button_UpdateStart.IsEnabled = true;
            CheckBox_IPv6Enable.IsEnabled = true;
            Button_UpdateStop.IsEnabled = false;
        }

        private void OnTimedEvent_UpdateDomain(object source, System.Timers.ElapsedEventArgs e)
        {
            UpdateDomain();
        }

        private void UpdateDomain()
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                Task.Run(() => UpdateDNSInfoAsync(MODE_IPV4));

                if (CheckBox_IPv6Enable.IsChecked == true)
                {
                    Task.Run(() => UpdateDNSInfoAsync(MODE_IPV6));
                }
            }));
        }

        private void Button_UpdateStart_Click(object sender, RoutedEventArgs e)
        {
            if (TextBox_APIKey.Text.Length > 0)
            {
                cfData.key = TextBox_APIKey.Text;
            }

            if (TextBox_ZoneID.Text.Length > 0)
            {
                cfData.zoneID = TextBox_ZoneID.Text;
            }

            if (TextBox_RecordIDv4.Text.Length > 0)
            {
                cfData.recordid_v4 = TextBox_RecordIDv4.Text;
            }

            if (TextBox_RecordIDv6.Text.Length > 0)
            {
                cfData.recordid_v6 = TextBox_RecordIDv6.Text;
            }

            if (TextBox_Interval.Text.Length > 0)
            {
                cfData.updateInterval = TextBox_Interval.Text;
            }

            cfData.enableIPv6 = (bool)CheckBox_IPv6Enable.IsChecked;

            recordIPv4 = "";
            recordIPv6 = "";

            UpdateDomain();

            StartTimer();

            SaveRecord();

        }

        private void Button_UpdateStop_Click(object sender, RoutedEventArgs e)
        {
            StopTimer();
        }

        private void CheckBox_IPv6Enable_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckBox_IPv6Enable.IsChecked == true)
            {
                SaveRecord();
            }
            else
            {
                Label_IPv6Status.Content = "IPv6";
                SaveRecord();
            }
        }


        private async void UpdateDNSInfoAsync(string currentMode)
        {
            try
            {
                Now = DateTime.Now.ToString();
                Uri uriForIPInfo;
                if (currentMode == MODE_IPV4)
                    uriForIPInfo = new Uri(ipInfoURL);
                else
                    uriForIPInfo = new Uri(ip6InfoURL);

                HttpClient clientForIPInfo = new HttpClient();
                HttpResponseMessage rspForIPInfo;
                rspForIPInfo = await clientForIPInfo.GetAsync(uriForIPInfo);
                rspForIPInfo.EnsureSuccessStatusCode();

                var newIP = await rspForIPInfo.Content.ReadAsStringAsync();

                if (currentMode == MODE_IPV4)
                {
                    if (newIP == recordIPv4)
                    {

                        UpdateStatsLabel(currentMode, "IP Not Changed");
                        return;
                    }
                    else
                        recordIPv4 = newIP.ToString();
                }
                else if (newIP == recordIPv6)
                {
                    UpdateStatsLabel(currentMode, "IP Not Changed");
                    return;
                }
                else if (newIP.Length > 0)
                {
                    string[] arr = newIP.Split(':');
                    if (arr.Length > 2)
                        recordIPv6 = newIP.ToString();
                    else
                    {
                        UpdateStatsLabel(currentMode, "IP Not Detect");
                    }
                }


                Dictionary<string, object> dataStruct = new Dictionary<string, object>();

                string authInfo = cfData.key + ":" + cfData.recordid_v4;
                string apiURL = "";

                UpdateStatsLabel(currentMode, newIP);

                if (currentMode == MODE_IPV4)
                {
                    apiURL = "https://api.cloudflare.com/client/v4/zones/" + cfData.zoneID + "/dns_records/" + cfData.recordid_v4;
                    dataStruct.Add("type", "A");
                    dataStruct.Add("name", "@");
                    dataStruct.Add("content", newIP);
                }
                else
                {
                    apiURL = "https://api.cloudflare.com/client/v4/zones/" + cfData.zoneID + "/dns_records/" + cfData.recordid_v6;
                    dataStruct.Add("type", "AAAA");
                    dataStruct.Add("name", "@");
                    dataStruct.Add("content", newIP);
                }

                dataStruct.Add("ttl", 1);
                dataStruct.Add("proxied", false);

                HttpClient client = new HttpClient();
                HttpResponseMessage rspn;
                Uri uriWebApi = new Uri(apiURL);
                string dataForPost = System.Text.Json.JsonSerializer.Serialize(dataStruct);
                HttpContent hContent = new StringContent(dataForPost, Encoding.UTF8, "application/json");
                AuthenticationHeaderValue token = new AuthenticationHeaderValue("Bearer", cfData.key);

                client.DefaultRequestHeaders.Authorization = token;
                rspn = await client.PutAsync(uriWebApi, hContent);
                rspn.EnsureSuccessStatusCode();

                 UpdateStatsLabel(currentMode, "Success");

            }
            catch (Exception ex)
            {
                string errorMsg = "";
                if (ex.Message != null)
                    errorMsg = ex.Message.Substring(0, Math.Min(ex.Message.Length, 40));
                UpdateStatsLabel(currentMode, errorMsg);
            }
            finally
            {
            }
        }


        private void UpdateStatsLabel(string mode, string info)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                if (mode == MODE_IPV4)
                    Label_IPv4Status.Content = "IPv4: " + info + " " + Now;
                else
                    Label_IPv6Status.Content = "IPv6: " + info + " " + Now;
            }));
          
        }

        private void LoadRecord()
        {
            if (File.Exists(recordFileName))
            {
                string rawString = File.ReadAllText(recordFileName);
                cfData.LoadDataFromString(rawString);
                System.Console.WriteLine(rawString);
                System.Console.WriteLine(cfData.recordid_v4);
            }
            else
            {
                cfData.updateInterval = "60";
            }

            TextBox_APIKey.Text = cfData.key;
            TextBox_ZoneID.Text = cfData.zoneID;
            TextBox_RecordIDv4.Text = cfData.recordid_v4;
            TextBox_RecordIDv6.Text = cfData.recordid_v6;
            TextBox_Interval.Text = cfData.updateInterval;
            CheckBox_IPv6Enable.IsChecked = cfData.enableIPv6;
        }

        private void SaveRecord()
        {
            if (File.Exists(recordFileName) == true)
                File.Delete(recordFileName);
            File.WriteAllText(recordFileName, cfData.StringForWrite());
        }

        #region TrayIcon

        private void InitTrayIcon()
        {
            m_notifyIcon = new System.Windows.Forms.NotifyIcon();
            m_notifyIcon.Text = "CFD";
            m_notifyIcon.Icon = Properties.Resources.godaddy;
            m_notifyIcon.MouseDoubleClick += mNotifyIcon_MouseDoubleClick;
            m_notifyIcon.MouseClick += mNotifyIcon_MouseClick;
            m_notifyIcon.Visible = true;

            mContextMenu = new System.Windows.Controls.ContextMenu();
            MenuItem itemMainWindowShow = new MenuItem();
            itemMainWindowShow.Header = "Setting";
            itemMainWindowShow.Click += new RoutedEventHandler(MainWindow_Show_Click);
            mContextMenu.Items.Add(itemMainWindowShow);
            MenuItem itemExit = new MenuItem();
            itemExit.Header = "Exit";
            itemExit.Click += new RoutedEventHandler(Exit_Click);
            mContextMenu.Items.Add(itemExit);
        }
        private void mNotifyIcon_MouseClick(object iSender, System.Windows.Forms.MouseEventArgs iEventArgs)
        {
            System.Windows.Forms.MouseEventArgs me = (System.Windows.Forms.MouseEventArgs)iEventArgs;
            if (iEventArgs.Button == System.Windows.Forms.MouseButtons.Right)
            {
                mContextMenu.IsOpen = true;
            }
        }
        private void MainWindow_Show_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
        }
        private void mNotifyIcon_MouseDoubleClick(object iSender, System.Windows.Forms.MouseEventArgs iEventArgs)
        {
            mContextMenu.IsOpen = false;
        }
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) this.Hide();
            base.OnStateChanged(e);
        }
        // Minimize to system tray when application is closed.
        protected override void OnClosing(CancelEventArgs e)
        {
            // setting cancel to true will cancel the close request
            // so the application is not closed
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

        #endregion
        
    }


    public class CloudFlareData
    {
        public string key;
        public string recordid_v4;
        public string zoneID;
        public string recordid_v6;
        public string updateInterval;
        public bool enableIPv6;

        private char _splitToken = ',';
        private int keyIndex = 0;
        private int secretIndex = 1;
        private int domainNameIndex = 2;
        private int hostNameIndex = 3;
        private int updateIntervalIndex = 4;
        private int enableIPv6Index = 5;
        public string StringForWrite()
        {
            System.Text.StringBuilder stringToWrite = new System.Text.StringBuilder();
            stringToWrite.Append(key);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(recordid_v4);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(zoneID);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(recordid_v6);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(updateInterval);
            stringToWrite.Append(_splitToken);
            stringToWrite.Append(enableIPv6.ToString());
            return stringToWrite.ToString();
        }

        public void LoadDataFromString(string input)
        {
            string[] datas = input.Split(_splitToken);
            key = datas[keyIndex];
            recordid_v4 = datas[secretIndex];
            zoneID = datas[domainNameIndex];
            recordid_v6 = datas[hostNameIndex];
            updateInterval = datas[updateIntervalIndex];
            if (Int32.Parse(updateInterval) > 35790)
                updateInterval = "35790";


            enableIPv6 = bool.Parse(datas[enableIPv6Index]);

        }

    }
}
