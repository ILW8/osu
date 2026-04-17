// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Writes live multiplayer room state to <c>ipc.json</c> under the tournament
    /// storage so external overlays and scoreboards can consume it by polling.
    /// Instantiated only when multiplayer spectating is active.
    /// </summary>
    internal partial class MultiplayerIPCWriter : Component
    {
        public const string IPC_DIRECTORY = "ipc";
        public const string IPC_FILENAME = "ipc.json";
        private const string ipc_tmp_filename = "ipc.json.tmp";

        [Resolved]
        private Storage storage { get; set; } = null!;

        private Storage ipcStorage = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);
            writeAtomically(IPCSnapshot.SerializeToJson(IPCSnapshot.EmptyDisconnected));
        }

        /// <summary>
        /// Serialize-to-temp + atomic rename so consumers never see a partial file.
        /// </summary>
        private void writeAtomically(string json)
        {
            string tmpFullPath = ipcStorage.GetFullPath(ipc_tmp_filename);
            string finalFullPath = ipcStorage.GetFullPath(IPC_FILENAME);

            try
            {
                File.WriteAllText(tmpFullPath, json);
                File.Move(tmpFullPath, finalFullPath, overwrite: true);
            }
            catch (IOException e)
            {
                Logger.Log($"[MultiplayerIPCWriter] Failed to write {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
        }
    }
}
