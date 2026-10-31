using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using WiPass.Core;

namespace WiPass.UI
{
    internal enum NoticeKind { Info, Warning, Error }

    internal sealed class NoticeButton
    {
        public string Label;
        public Action Action;
        public NoticeButton(string label, Action action) { Label = label; Action = action; }
    }

    /// <summary>
    /// Windows notifications. Uses real Windows toasts (app icon, buttons) when WiPass is installed
    /// with its Start menu shortcut — Windows needs that shortcut's app ID to show toasts for a
    /// desktop program. Otherwise (portable copy) falls back to the classic tray balloon.
    /// WinRT is called by reflection so no extra packages are needed on .NET Framework 4.8.
    /// </summary>
    internal sealed class Notifier
    {
        public const string AppId = "jomwalimu.WiPass";
        public const string ShortcutName = "와이패스 (WiPass).lnk";
        private const int KeepToasts = 8;

        private readonly NotifyIcon tray;
        private readonly Action<Action> post;
        private readonly Dictionary<string, Action[]> actions = new Dictionary<string, Action[]>();
        private readonly Queue<string> order = new Queue<string>();
        private readonly List<object> alive = new List<object>(); // keep toast objects so their events fire
        private Action balloonClick;

        private bool toastReady;
        private Type xmlType, toastType;
        private object notifier;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        /// <summary>Gives this process the same app ID as the installer's Start menu shortcut.</summary>
        public static void RegisterProcessAppId()
        {
            try { SetCurrentProcessExplicitAppUserModelID(AppId); } catch { }
        }

        public Notifier(NotifyIcon tray, Action<Action> post)
        {
            this.tray = tray;
            this.post = post;
            tray.BalloonTipClicked += (s, e) => { var a = balloonClick; balloonClick = null; if (a != null) a(); };
            tray.BalloonTipClosed += (s, e) => balloonClick = null;

            try
            {
                string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
                if (!File.Exists(shortcut))
                {
                    Log.Info("시작 메뉴 바로가기가 없어 기본 알림을 사용합니다 (압축판)");
                    return;
                }
                Type manager = Type.GetType("Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime", true);
                xmlType = Type.GetType("Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType=WindowsRuntime", true);
                toastType = Type.GetType("Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType=WindowsRuntime", true);
                notifier = manager.GetMethod("CreateToastNotifier", new[] { typeof(string) }).Invoke(null, new object[] { AppId });
                toastReady = true;
            }
            catch (Exception ex)
            {
                Log.Warn("윈도우 알림을 쓸 수 없어 기본 알림을 사용합니다: " + Inner(ex).Message);
            }
        }

        /// <summary>
        /// Shows a notification. <paramref name="onClick"/> runs when the notification itself is clicked;
        /// buttons appear on toasts only (on the balloon, a click runs the first button).
        /// </summary>
        public void Show(string title, string body, NoticeKind kind, Action onClick = null, params NoticeButton[] buttons)
        {
            if (buttons == null) buttons = new NoticeButton[0];
            if (toastReady)
            {
                try { ShowToast(title, body, onClick, buttons); return; }
                catch (Exception ex)
                {
                    Log.Warn("윈도우 알림 표시 실패, 기본 알림으로 전환: " + Inner(ex).Message);
                    toastReady = false;
                }
            }
            balloonClick = onClick ?? (buttons.Length > 0 ? buttons[0].Action : null);
            ToolTipIcon icon = kind == NoticeKind.Error ? ToolTipIcon.Error : kind == NoticeKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info;
            tray.ShowBalloonTip(5000, title, body, icon);
        }

        private void ShowToast(string title, string body, Action onClick, NoticeButton[] buttons)
        {
            string id = Guid.NewGuid().ToString("N").Substring(0, 10);
            var xml = new StringBuilder();
            xml.Append("<toast launch=\"id=").Append(id).Append(";b=-1\" activationType=\"foreground\">");
            xml.Append("<visual><binding template=\"ToastGeneric\">");
            xml.Append("<text>").Append(SecurityElement.Escape(title)).Append("</text>");
            // ToastGeneric shows up to three text lines: the title plus two body lines.
            string[] lines = (body ?? "").Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0) xml.Append("<text>").Append(SecurityElement.Escape(lines[0])).Append("</text>");
            if (lines.Length > 1) xml.Append("<text>").Append(SecurityElement.Escape(string.Join(" ", lines, 1, lines.Length - 1))).Append("</text>");
            xml.Append("</binding></visual>");
            if (buttons.Length > 0)
            {
                xml.Append("<actions>");
                for (int i = 0; i < buttons.Length && i < 3; i++)
                    xml.Append("<action content=\"").Append(SecurityElement.Escape(buttons[i].Label))
                       .Append("\" arguments=\"id=").Append(id).Append(";b=").Append(i).Append("\" activationType=\"foreground\"/>");
                xml.Append("</actions>");
            }
            xml.Append("</toast>");

            object doc = Activator.CreateInstance(xmlType);
            xmlType.GetMethod("LoadXml", new[] { typeof(string) }).Invoke(doc, new object[] { xml.ToString() });
            object toast = toastType.GetConstructor(new[] { xmlType }).Invoke(new[] { doc });

            // One WiPass notification at a time in the notification center: a new one replaces the old.
            TrySet(toast, "Tag", "wipass");
            TrySet(toast, "Group", "status");
            TrySet(toast, "ExpirationTime", (DateTimeOffset?)DateTimeOffset.Now.AddHours(1));

            var list = new Action[buttons.Length + 1];
            list[0] = onClick;
            for (int i = 0; i < buttons.Length; i++) list[i + 1] = buttons[i].Action;
            Remember(id, list, toast);

            EventInfo activated = toastType.GetEvent("Activated");
            MethodInfo handler = typeof(Notifier).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance);
            activated.AddEventHandler(toast, Delegate.CreateDelegate(activated.EventHandlerType, this, handler));

            notifier.GetType().GetMethod("Show", new[] { toastType }).Invoke(notifier, new[] { toast });
        }

        private void Remember(string id, Action[] list, object toast)
        {
            lock (actions)
            {
                actions[id] = list;
                order.Enqueue(id);
                alive.Add(toast);
                while (order.Count > KeepToasts) actions.Remove(order.Dequeue());
                while (alive.Count > KeepToasts) alive.RemoveAt(0);
            }
        }

        // Signature matches TypedEventHandler<ToastNotification, object> through relaxed delegate binding.
        private void OnActivated(object sender, object args)
        {
            try
            {
                string arguments = null;
                if (args != null)
                {
                    PropertyInfo prop = args.GetType().GetProperty("Arguments");
                    if (prop != null) arguments = prop.GetValue(args, null) as string;
                }
                string id = null;
                int button = -1;
                foreach (string part in (arguments ?? "").Split(';'))
                {
                    if (part.StartsWith("id=")) id = part.Substring(3);
                    else if (part.StartsWith("b=")) int.TryParse(part.Substring(2), out button);
                }
                Action[] list = null;
                lock (actions) { if (id != null) actions.TryGetValue(id, out list); }
                if (list == null) return;
                Action a = button + 1 >= 0 && button + 1 < list.Length ? list[button + 1] : null;
                if (a != null) post(a);
            }
            catch (Exception ex)
            {
                Log.Warn("알림 클릭 처리 오류: " + ex.Message);
            }
        }

        private static void TrySet(object target, string property, object value)
        {
            try
            {
                PropertyInfo p = target.GetType().GetProperty(property);
                if (p != null && p.CanWrite) p.SetValue(target, value, null);
            }
            catch { }
        }

        private static Exception Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
