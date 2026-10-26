using System;
using System.Security;
using System.Text;
using System.Xml;

namespace WiPass.Wifi
{
    internal enum SecurityKind { Open, Personal, Enterprise, Unknown }

    /// <summary>Builds Windows WLAN profile XML for "same name, different password" networks.</summary>
    internal static class ProfileBuilder
    {
        // DOT11_AUTH_ALGORITHM values
        private const int AUTH_OPEN = 1, AUTH_SHARED = 2, AUTH_WPA = 3, AUTH_WPA_PSK = 4, AUTH_WPA_NONE = 5,
                          AUTH_RSNA = 6, AUTH_RSNA_PSK = 7, AUTH_WPA3 = 8, AUTH_WPA3_SAE = 9, AUTH_OWE = 10, AUTH_WPA3_ENT = 11;
        private const int CIPHER_TKIP = 2;

        public static SecurityKind Classify(int auth)
        {
            switch (auth)
            {
                case AUTH_OPEN:
                case AUTH_OWE:
                    return SecurityKind.Open;
                case AUTH_WPA_PSK:
                case AUTH_RSNA_PSK:
                case AUTH_WPA3_SAE:
                    return SecurityKind.Personal;
                case AUTH_WPA:
                case AUTH_RSNA:
                case AUTH_WPA3:
                case AUTH_WPA3_ENT:
                    return SecurityKind.Enterprise;
                default:
                    return SecurityKind.Unknown;
            }
        }

        public static string Describe(int auth)
        {
            switch (auth)
            {
                case AUTH_OPEN: return "비밀번호 없음";
                case AUTH_OWE: return "비밀번호 없음(OWE)";
                case AUTH_SHARED: return "WEP";
                case AUTH_WPA_PSK: return "WPA-개인";
                case AUTH_RSNA_PSK: return "WPA2-개인";
                case AUTH_WPA3_SAE: return "WPA3-개인";
                case AUTH_WPA: return "WPA-엔터프라이즈";
                case AUTH_RSNA: return "WPA2-엔터프라이즈";
                case AUTH_WPA3:
                case AUTH_WPA3_ENT: return "WPA3-엔터프라이즈";
                default: return "알 수 없음(" + auth + ")";
            }
        }

        /// <summary>Returns an error message, or null if the password is acceptable for WPA/WPA2/WPA3-Personal.</summary>
        public static string ValidatePassword(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return "비밀번호를 입력해 주세요.";
            if (IsHexKey(pw)) return null;
            if (pw.Length < 8 || pw.Length > 63) return "와이파이 비밀번호는 8~63자입니다.";
            return null;
        }

        private static bool IsHexKey(string pw)
        {
            if (pw.Length != 64) return false;
            foreach (char c in pw)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        public static string SsidHex(string ssid)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(ssid ?? "")) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        /// <summary>
        /// Turns an existing saved profile into a reusable template (password removed).
        /// Returns null if the profile is not a personal (pre-shared key) profile.
        /// </summary>
        public static string MakeTemplate(string profileXml)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(profileXml);
                XmlNode auth = doc.SelectSingleNode("//*[local-name()='authEncryption']/*[local-name()='authentication']");
                if (auth == null) return null;
                string a = auth.InnerText.Trim().ToUpperInvariant();
                if (a != "WPAPSK" && a != "WPA2PSK" && a != "WPA3SAE") return null;
                XmlNode key = doc.SelectSingleNode("//*[local-name()='sharedKey']");
                if (key == null) return null;
                SetChild(key, "keyMaterial", "");
                SetChild(key, "protected", "false");
                return doc.OuterXml;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>Fills a template with the SSID name and password. Returns null if the template is unusable.</summary>
        public static string FromTemplate(string template, string ssid, string password)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(template);
                XmlNode root = doc.DocumentElement;
                XmlNode name = root.SelectSingleNode("*[local-name()='name']");
                if (name != null) name.InnerText = ssid;
                XmlNode mode = root.SelectSingleNode("*[local-name()='connectionMode']");
                if (mode != null) mode.InnerText = "auto";
                XmlNode key = doc.SelectSingleNode("//*[local-name()='sharedKey']");
                if (key == null) return null;
                SetChild(key, "keyType", IsHexKey(password) ? "networkKey" : "passPhrase");
                SetChild(key, "protected", "false");
                SetChild(key, "keyMaterial", password);
                return doc.OuterXml;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>Creates a fresh profile from the security type Windows reports for the visible network.</summary>
        public static string Create(string ssid, int auth, int cipher, string password)
        {
            string a = auth == AUTH_WPA3_SAE ? "WPA3SAE" : auth == AUTH_WPA_PSK ? "WPAPSK" : "WPA2PSK";
            string e = cipher == CIPHER_TKIP ? "TKIP" : "AES";
            string keyType = IsHexKey(password) ? "networkKey" : "passPhrase";
            string n = SecurityElement.Escape(ssid);
            return
                "<?xml version=\"1.0\"?>" +
                "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
                "<name>" + n + "</name>" +
                "<SSIDConfig><SSID><hex>" + SsidHex(ssid) + "</hex><name>" + n + "</name></SSID></SSIDConfig>" +
                "<connectionType>ESS</connectionType>" +
                "<connectionMode>auto</connectionMode>" +
                "<MSM><security>" +
                "<authEncryption><authentication>" + a + "</authentication><encryption>" + e + "</encryption><useOneX>false</useOneX></authEncryption>" +
                "<sharedKey><keyType>" + keyType + "</keyType><protected>false</protected><keyMaterial>" + SecurityElement.Escape(password) + "</keyMaterial></sharedKey>" +
                "</security></MSM>" +
                "</WLANProfile>";
        }

        private static void SetChild(XmlNode parent, string localName, string value)
        {
            XmlNode n = parent.SelectSingleNode("*[local-name()='" + localName + "']");
            if (n == null)
            {
                n = parent.OwnerDocument.CreateElement(localName, parent.NamespaceURI);
                parent.AppendChild(n);
            }
            n.InnerText = value;
        }
    }
}
