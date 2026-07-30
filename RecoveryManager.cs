using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WritingApp
{
    internal sealed class RecoverySnapshot
    {
        public string RecoveryPath;
        public string OriginalPath;
        public bool IsRichText;
        public string Content;
        public DateTime SavedAt;
    }

    internal static class RecoveryManager
    {
        private const string Magic = "QWR1";

        private static string RecoveryFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WritingApp",
                    "Recovery");
            }
        }

        public static string CreateRecoveryPath()
        {
            Directory.CreateDirectory(RecoveryFolder);
            return Path.Combine(RecoveryFolder, Guid.NewGuid().ToString("N") + ".qwr");
        }

        public static void Save(
            string recoveryPath,
            string originalPath,
            bool isRichText,
            string content)
        {
            string folder = Path.GetDirectoryName(recoveryPath);
            if (!String.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            using (var stream = new FileStream(
                recoveryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(originalPath ?? String.Empty);
                writer.Write(isRichText);
                writer.Write(content ?? String.Empty);
                writer.Write(DateTime.UtcNow.Ticks);
            }
        }

        public static List<RecoverySnapshot> LoadAll()
        {
            var snapshots = new List<RecoverySnapshot>();
            if (!Directory.Exists(RecoveryFolder))
                return snapshots;

            foreach (string path in Directory.GetFiles(RecoveryFolder, "*.qwr"))
            {
                try
                {
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                    using (var reader = new BinaryReader(stream, Encoding.UTF8))
                    {
                        if (reader.ReadString() != Magic)
                            continue;

                        snapshots.Add(new RecoverySnapshot
                        {
                            RecoveryPath = path,
                            OriginalPath = reader.ReadString(),
                            IsRichText = reader.ReadBoolean(),
                            Content = reader.ReadString(),
                            SavedAt = new DateTime(reader.ReadInt64(), DateTimeKind.Utc)
                        });
                    }
                }
                catch
                {
                    // A damaged recovery file must not prevent WritingApp from starting.
                }
            }

            snapshots.Sort(delegate(RecoverySnapshot first, RecoverySnapshot second)
            {
                return first.SavedAt.CompareTo(second.SavedAt);
            });
            return snapshots;
        }

        public static void Delete(string recoveryPath)
        {
            if (String.IsNullOrEmpty(recoveryPath))
                return;
            try
            {
                if (File.Exists(recoveryPath))
                    File.Delete(recoveryPath);
            }
            catch
            {
                // Recovery cleanup should never block normal document work.
            }
        }
    }
}
