using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace WiPass.Core
{
    [DataContract]
    internal sealed class Place
    {
        [DataMember(Order = 1)] public string Id;
        [DataMember(Order = 2)] public string Name;
        /// <summary>DPAPI-encrypted, base64.</summary>
        [DataMember(Order = 3)] public string ProtectedPassword;
        /// <summary>Access point MAC addresses seen while connected at this place.</summary>
        [DataMember(Order = 4)] public List<string> Bssids;
        /// <summary>ISO 8601, UTC.</summary>
        [DataMember(Order = 5)] public string LastUsedUtc;

        public bool HasPassword { get { return !string.IsNullOrEmpty(ProtectedPassword); } }

        public Place Clone()
        {
            return new Place
            {
                Id = Id,
                Name = Name,
                ProtectedPassword = ProtectedPassword,
                Bssids = new List<string>(Bssids ?? new List<string>()),
                LastUsedUtc = LastUsedUtc
            };
        }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        public const int MaxBssidsPerPlace = 300;

        [DataMember(Order = 1)] public int Version = 1;
        /// <summary>The shared network name (SSID) used at every place.</summary>
        [DataMember(Order = 2)] public string Ssid;
        [DataMember(Order = 3)] public List<Place> Places;
        [DataMember(Order = 4)] public string CurrentPlaceId;
        /// <summary>Profile XML copied from the user's own saved network, password removed.</summary>
        [DataMember(Order = 5)] public string ProfileTemplate;

        public bool IsConfigured
        {
            get { return !string.IsNullOrEmpty(Ssid) && Places != null && Places.Count > 0; }
        }

        public Place FindPlace(string id)
        {
            if (id == null || Places == null) return null;
            foreach (var p in Places) if (p.Id == id) return p;
            return null;
        }

        /// <summary>Place whose remembered access points include <paramref name="bssid"/>.</summary>
        public Place FindPlaceByBssid(string bssid)
        {
            if (bssid == null || Places == null) return null;
            foreach (var p in Places)
                if (p.Bssids != null && p.Bssids.Contains(bssid)) return p;
            return null;
        }

        /// <summary>Remember access points for a place; an access point belongs to one place only.</summary>
        public void Learn(Place place, IEnumerable<string> bssids)
        {
            var add = new List<string>();
            foreach (var b in bssids)
                if (!string.IsNullOrEmpty(b) && !add.Contains(b)) add.Add(b);
            if (add.Count == 0) return;
            foreach (var other in Places)
                if (other != place && other.Bssids != null)
                    other.Bssids.RemoveAll(add.Contains);
            if (place.Bssids == null) place.Bssids = new List<string>();
            foreach (var b in add)
                if (!place.Bssids.Contains(b)) place.Bssids.Add(b);
            if (place.Bssids.Count > MaxBssidsPerPlace)
                place.Bssids.RemoveRange(0, place.Bssids.Count - MaxBssidsPerPlace);
        }

        public AppSettings Clone()
        {
            var c = new AppSettings
            {
                Version = Version,
                Ssid = Ssid,
                CurrentPlaceId = CurrentPlaceId,
                ProfileTemplate = ProfileTemplate,
                Places = new List<Place>()
            };
            if (Places != null) foreach (var p in Places) c.Places.Add(p.Clone());
            return c;
        }

        private void Normalize()
        {
            if (Places == null) Places = new List<Place>();
            Places.RemoveAll(p => p == null);
            foreach (var p in Places)
            {
                if (string.IsNullOrEmpty(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                if (p.Bssids == null) p.Bssids = new List<string>();
                if (p.Name == null) p.Name = "";
            }
        }

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    using (var fs = File.OpenRead(AppPaths.SettingsFile))
                        s = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs);
                }
            }
            catch (Exception ex)
            {
                Log.Error("설정 파일을 읽지 못했습니다: " + ex.Message);
                try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".broken", true); } catch { }
            }
            if (s == null) s = new AppSettings();
            s.Normalize();
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                string tmp = AppPaths.SettingsFile + ".tmp";
                using (var fs = File.Create(tmp))
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(fs, this);
                if (File.Exists(AppPaths.SettingsFile))
                    File.Replace(tmp, AppPaths.SettingsFile, null);
                else
                    File.Move(tmp, AppPaths.SettingsFile);
            }
            catch (Exception ex)
            {
                Log.Error("설정 저장 실패: " + ex.Message);
            }
        }
    }
}
