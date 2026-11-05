using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace WiPass.Core
{
    /// <summary>
    /// Lets a second launch talk to the running WiPass instead of starting another copy:
    /// opening WiPass again (Start menu, desktop) shows the quick panel, and a notification
    /// click that Windows delivers as a new launch is handed to the running instance.
    /// Only the same Windows user on this PC can connect.
    /// </summary>
    internal static class SingleInstance
    {
        private static string PipeName
        {
            get
            {
                string sid = "user";
                try { sid = WindowsIdentity.GetCurrent().User.Value; } catch { }
                return "WiPass.Activate." + sid;
            }
        }

        /// <summary>Sends the launch arguments to the running instance. False if it could not be reached.</summary>
        public static bool SendToRunning(string args)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(1500);
                    byte[] data = Encoding.UTF8.GetBytes((args ?? "") + "\n");
                    client.Write(data, 0, data.Length);
                    client.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("실행 중인 와이패스에 연결하지 못함: " + ex.Message);
                return false;
            }
        }

        /// <summary>Listens on a background thread; <paramref name="onMessage"/> is called with each launch's arguments.</summary>
        public static void Listen(Action<string> onMessage)
        {
            var t = new Thread(() =>
            {
                string name = PipeName;
                while (true)
                {
                    try
                    {
                        using (NamedPipeServerStream server = Create(name))
                        {
                            server.WaitForConnection();
                            var buf = new byte[4096];
                            int n = server.Read(buf, 0, buf.Length);
                            string msg = Encoding.UTF8.GetString(buf, 0, Math.Max(0, n)).Trim();
                            onMessage(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("실행 요청 수신 오류: " + ex.Message);
                        Thread.Sleep(2000);
                    }
                }
            });
            t.IsBackground = true;
            t.Name = "WiPass activation";
            t.Start();
        }

        private static NamedPipeServerStream Create(string name)
        {
            var security = new PipeSecurity();
            SecurityIdentifier me = WindowsIdentity.GetCurrent().User;
            security.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security);
        }
    }
}
