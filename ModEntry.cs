using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;

namespace RandomJumpscares
{
    public class ModConfig
    {
        public float ChancePercent { get; set; } = 25f;
    }

    public static class I18n
    {
        private static ITranslationHelper H = null!;
        public static void Init(ITranslationHelper helper) => H = helper;
        public static string Command_ShowJumpscare_Desc() => H.Get("OMEGAlinc.RandomJumpscares.command.showjumpscare.desc");
        public static string Command_ShowJumpscare_Usage() => H.Get("OMEGAlinc.RandomJumpscares.command.showjumpscare.usage");
        public static string Error_LoadSaveRequired() => H.Get("OMEGAlinc.RandomJumpscares.error.loadsave_required");
        public static string Error_UnknownId(string id, string available) => H.Get("OMEGAlinc.RandomJumpscares.error.unknown_id", new { id, available });
        public static string Error_ImageMissing(string id) => H.Get("OMEGAlinc.RandomJumpscares.error.image_missing", new { id });
    }

    public class ModEntry : Mod
    {
        private const string Scream1Id = "OMEGAlinc.RandomJumpscares_Scream1";
        private const string Scream2Id = "OMEGAlinc.RandomJumpscares_Scream2";
        private const string Scream3Id = "OMEGAlinc.RandomJumpscares_Scream3";
        private const string Scream4Id = "OMEGAlinc.RandomJumpscares_Scream4";
        private const int ShowDurationTicks = 180;

        private sealed class JumpscareDef
        {
            public string SoundId = "";
            public string ImageAssetPath = "";
        }

        private readonly Dictionary<string, JumpscareDef> _jumpscares = new()
        {
            { Scream1Id, new() { SoundId = Scream1Id, ImageAssetPath = "assets/scaryimage1.png" } },
            { Scream2Id, new() { SoundId = Scream2Id, ImageAssetPath = "assets/scaryimage2.png" } },
            { Scream3Id, new() { SoundId = Scream3Id, ImageAssetPath = "assets/scaryimage3.png" } },
            { Scream4Id, new() { SoundId = Scream4Id, ImageAssetPath = "assets/scaryimage4.png" } }
        };

        private readonly Dictionary<string, Texture2D> _loadedTextures = new();
        private readonly Random _rng = new();
        private bool _isShowing;
        private int _showTicksRemaining;
        private Texture2D? _activeTexture;
        private ModConfig _config = new();
        private double _chancePer10Min;

        public override void Entry(IModHelper helper)
        {
            I18n.Init(helper.Translation);

            _config = helper.ReadConfig<ModConfig>();
            ApplyConfig();

            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Display.RenderedHud += OnRenderedHud;

            helper.ConsoleCommands.Add(
                "showjumpscare",
                I18n.Command_ShowJumpscare_Desc(),
                OnShowJumpscareCommand
            );
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!e.Name.IsEquivalentTo("Data/AudioChanges"))
                return;

            e.Edit(asset =>
            {
                var data = asset.AsDictionary<string, AudioCueData>().Data;

                List<string> scream1Paths = new() { Path.Combine(this.Helper.DirectoryPath, "assets", "scream1.wav") };
                data[Scream1Id] = new AudioCueData { Id = Scream1Id, Category = "Sound", FilePaths = scream1Paths, StreamedVorbis = false, Looped = false, UseReverb = false, CustomFields = new() };

                List<string> scream2Paths = new() { Path.Combine(this.Helper.DirectoryPath, "assets", "scream2.wav") };
                data[Scream2Id] = new AudioCueData { Id = Scream2Id, Category = "Sound", FilePaths = scream2Paths, StreamedVorbis = false, Looped = false, UseReverb = false, CustomFields = new() };

                List<string> scream3Paths = new() { Path.Combine(this.Helper.DirectoryPath, "assets", "scream3.wav") };
                data[Scream3Id] = new AudioCueData { Id = Scream3Id, Category = "Sound", FilePaths = scream3Paths, StreamedVorbis = false, Looped = false, UseReverb = false, CustomFields = new() };

                List<string> scream4Paths = new() { Path.Combine(this.Helper.DirectoryPath, "assets", "scream4.wav") };
                data[Scream4Id] = new AudioCueData { Id = Scream4Id, Category = "Sound", FilePaths = scream4Paths, StreamedVorbis = false, Looped = false, UseReverb = false, CustomFields = new() };
            });
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            _loadedTextures.Clear();
            foreach (var (id, def) in _jumpscares)
            {
                try
                {
                    var tex = this.Helper.ModContent.Load<Texture2D>(def.ImageAssetPath);
                    _loadedTextures[id] = tex;
                }
                catch { }
            }
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady || _isShowing || _loadedTextures.Count == 0)
                return;

            if (_rng.NextDouble() < _chancePer10Min)
            {
                var list = _loadedTextures.ToList();
                var (id, tex) = list[_rng.Next(list.Count)];
                var soundId = _jumpscares[id].SoundId;
                TriggerJumpscare(tex, soundId);
            }
        }

        private void OnShowJumpscareCommand(string name, string[] args)
        {
            if (!Context.IsWorldReady)
            {
                this.Monitor.Log(I18n.Error_LoadSaveRequired(), LogLevel.Warn);
                return;
            }

            if (args.Length != 1)
            {
                this.Monitor.Log(I18n.Command_ShowJumpscare_Usage(), LogLevel.Info);
                return;
            }

            var id = args[0];
            if (!_jumpscares.ContainsKey(id))
            {
                var available = string.Join(", ", _jumpscares.Keys);
                this.Monitor.Log(I18n.Error_UnknownId(id, available), LogLevel.Warn);
                return;
            }

            if (!_loadedTextures.TryGetValue(id, out var tex))
            {
                this.Monitor.Log(I18n.Error_ImageMissing(id), LogLevel.Warn);
                return;
            }

            var soundId = _jumpscares[id].SoundId;
            TriggerJumpscare(tex, soundId);
        }

        private void TriggerJumpscare(Texture2D texture, string soundId)
        {
            _activeTexture = texture;
            _isShowing = true;
            _showTicksRemaining = ShowDurationTicks;
            Game1.playSound(soundId);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!_isShowing)
                return;

            if (_showTicksRemaining > 0)
                _showTicksRemaining--;
            else
            {
                _isShowing = false;
                _activeTexture = null;
            }
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (!_isShowing || _activeTexture is not Texture2D tex)
                return;

            var sb = Game1.spriteBatch;
            var vp = Game1.graphics.GraphicsDevice.Viewport;
            int screenW = vp.Width, screenH = vp.Height;
            int imgW = tex.Width, imgH = tex.Height;

            float scale = Math.Max((float)screenW / imgW, (float)screenH / imgH);
            int drawW = (int)(imgW * scale), drawH = (int)(imgH * scale);
            int drawX = (screenW - drawW) / 2, drawY = (screenH - drawH) / 2;

            sb.Draw(tex, new Rectangle(drawX, drawY, drawW, drawH), Color.White);
        }

        private void ApplyConfig()
        {
            var p = _config?.ChancePercent ?? 25f;
            if (p < 0f) p = 0f;
            if (p > 100f) p = 100f;
            _config.ChancePercent = p;
            _chancePer10Min = p / 100.0;
            this.Helper.WriteConfig(_config);
        }

        private void ResetConfig()
        {
            _config = new();
            ApplyConfig();
        }

        private void SaveConfig()
        {
            ApplyConfig();
        }
    }
}
