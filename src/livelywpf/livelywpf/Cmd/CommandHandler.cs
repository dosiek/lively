﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace livelywpf.Cmd
{
    class CommandHandler
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        [Verb("app", isDefault:true, HelpText = "Application controls.")]
        class AppOptions
        {
            [Option("showApp",
            Required = false,
            HelpText = "Open app window (true/false).")]
            public bool? ShowApp { get; set; }

            [Option("showIcons",
            Required = false,
            HelpText = "Desktop icons visibility (true/false).")]
            public bool? ShowIcons { get; set; }
        }

        [Verb("control", HelpText = "Wallpaper control.")]
        class ControlOptions
        {
            [Option("volume",
            Required = false,
            HelpText = "Wallpaper audio level (0-100).")]
            public int? Volume { get; set; }

            [Option("play",
            Required = false,
            HelpText = "Wallpaper playback state (true/false).")]
            public bool? Play { get; set; }

            [Option("closewp",
            Required = false,
            HelpText = "Close wallpaper on the given monitor id, if -1 all wallpapers are closed.")]
            public int? Close { get; set; }
        }

        [Verb("setwp", HelpText = "Apply wallpaper.")]
        class SetWallpaperOptions
        {
            [Option("file",
            Required = true,
            HelpText = "Path containing LivelyInfo.json project file.")]
            public string File { get; set; }

            [Option("monitor",
            Required = false,
            HelpText = "Index of the monitor to load the wallpaper on (optional).")]
            public int? Monitor { get; set; }
        }

        [Verb("cuzwp", HelpText = "Customise wallpaper property.")]
        class CustomiseWallpaperOptions
        {
            [Option("property",
            Required = true,
            HelpText = "syntax: keyvalue=value")]
            public string Param { get; set; }

            [Option("monitor",
            Required = false,
            HelpText = "Index of the monitor to apply the wallpaper customisation.")]
            public int? Monitor { get; set; }
        }

        public static void ParseArgs(string[] args)
        {
            _ = CommandLine.Parser.Default.ParseArguments<ControlOptions, SetWallpaperOptions, AppOptions, CustomiseWallpaperOptions>(args)
                .MapResult(
                    (AppOptions opts) => RunAppOptions(opts),
                    (ControlOptions opts) => RunControlOptions(opts),
                    (SetWallpaperOptions opts) => RunSetWallpaperOptions(opts),
                    (CustomiseWallpaperOptions opts) => RunCustomiseWallpaperOptions(opts),
                    errs => HandleParseError(errs));
        }

        private static int RunAppOptions(AppOptions opts)
        {
            if (opts.ShowApp != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(delegate
                {
                    if ((bool)opts.ShowApp)
                    {
                        Program.ShowMainWindow();
                    }
                    else
                    {
                        App.AppWindow?.HideWindow();
                    }
                }));
            }

            if (opts.ShowIcons != null)
            {
                Helpers.DesktopUtil.SetDesktopIconVisibility((bool)opts.ShowIcons);
            }
            return 0;
        }

        private static int RunControlOptions(ControlOptions opts)
        {
            if (opts.Volume != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(delegate
                {
                    Program.SettingsVM.GlobalWallpaperVolume = Clamp((int)opts.Volume, 0, 100);
                }));
            }

            if (opts.Play != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(delegate
                {
                    Core.Playback.WallpaperPlaybackState = (bool)opts.Play ? PlaybackState.play : PlaybackState.paused;
                }));
            }

            if (opts.Close != null)
            {
                var id = (int)opts.Close;
                if (id == -1 || 
                    Program.SettingsVM.Settings.WallpaperArrangement == WallpaperArrangement.duplicate ||
                    Program.SettingsVM.Settings.WallpaperArrangement == WallpaperArrangement.span)
                {
                    SetupDesktop.CloseAllWallpapers();
                }
                else
                {
                    var screen = ScreenHelper.GetScreen().FirstOrDefault(x => x.DeviceNumber == (id).ToString());
                    if (screen != null)
                    {
                        SetupDesktop.CloseWallpaper(screen);
                    }
                }
            }
            return 0;
        }

        private static int RunSetWallpaperOptions(SetWallpaperOptions opts)
        {
            if (opts.File != null)
            {
                if (Directory.Exists(opts.File))
                {
                    //Folder containing LivelyInfo.json file.
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new ThreadStart(delegate
                    {
                        Core.LivelyScreen screen = opts.Monitor != null ?
                            ScreenHelper.GetScreen().FirstOrDefault(x => x.DeviceNumber == ((int)opts.Monitor).ToString()) : ScreenHelper.GetPrimaryScreen();

                        var libraryItem = Program.LibraryVM.LibraryItems.FirstOrDefault(x => x.LivelyInfoFolderPath.Equals(opts.File));
                        if (libraryItem != null && screen != null)
                        {
                            Program.LibraryVM.WallpaperSet(libraryItem, screen);
                        }
                    }));
                }
                else if (File.Exists(opts.File))
                {
                    //todo: load wallpaper file(video, website etc..) -> create quick thumbnail without user input -> set as wallpaper.
                    //related: https://github.com/rocksdanister/lively/issues/273 (Batch wallpaper import.) 
                }

            }
            return 0;
        }

        private static int RunCustomiseWallpaperOptions(CustomiseWallpaperOptions opts)
        {
            if (opts.Param != null)
            {
                Core.LivelyScreen screen = opts.Monitor != null ?
                    ScreenHelper.GetScreen().FirstOrDefault(x => x.DeviceNumber == ((int)opts.Monitor).ToString()) : ScreenHelper.GetPrimaryScreen();

                if (screen != null)
                {
                    try
                    {
                        var wp = SetupDesktop.Wallpapers.Find(x => ScreenHelper.ScreenCompare(x.GetScreen(), screen, DisplayIdentificationMode.screenLayout));
                        //delimiter
                        var tmp = opts.Param.Split("=");
                        string name = tmp[0], val = tmp[1], ctype = null;
                        var lp = JObject.Parse(File.ReadAllText(wp.GetLivelyPropertyCopyPath()));
                        foreach (var item in lp)
                        {
                            //Searching for the given control in the json file.
                            if (item.Key.ToString().Equals(name, StringComparison.Ordinal))
                            {
                                ctype = item.Value["type"].ToString();
                                val = ctype.Equals("folderDropdown", StringComparison.OrdinalIgnoreCase) ?
                                    Path.Combine(item.Value["folder"].ToString(), val) : val;
                                break;
                            }
                        }

                        ctype = (ctype == null && name.Equals("lively_default_settings_reload", StringComparison.OrdinalIgnoreCase)) ? "button" : ctype;
                        if (ctype != null)
                        {
                            if (ctype.Equals("button", StringComparison.OrdinalIgnoreCase))
                            {
                                if (name.Equals("lively_default_settings_reload", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (Cef.LivelyPropertiesView.RestoreOriginalPropertyFile(wp.GetWallpaperData(), wp.GetLivelyPropertyCopyPath()))
                                    {
                                        SetupDesktop.SendMessageWallpaper(screen, "lively:customise button lively_default_settings_reload 1");
                                    }
                                }
                                else
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + val);
                                }
                            }
                            else
                            {
                                if (ctype.Equals("checkbox", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + (val == "true"));
                                    lp[name]["value"] = (val == "true");
                                }
                                else if (ctype.Equals("slider", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + double.Parse(val));
                                    lp[name]["value"] = double.Parse(val);
                                }
                                else if (ctype.Equals("dropdown", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + int.Parse(val));
                                    lp[name]["value"] = int.Parse(val);
                                }
                                else if (ctype.Equals("folderDropdown", StringComparison.OrdinalIgnoreCase) ||
                                         ctype.Equals("textbox", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + "\"" + val + "\"");
                                    lp[name]["value"] = val;
                                }
                                else if (ctype.Equals("color", StringComparison.OrdinalIgnoreCase))
                                {
                                    SetupDesktop.SendMessageWallpaper(screen, "lively:customise " + ctype + " " + name + " " + val);
                                    lp[name]["value"] = val;
                                }

                                //Saving changes to copy file.
                                Cef.LivelyPropertiesJSON.SaveLivelyProperties(wp.GetLivelyPropertyCopyPath(), lp);
                            }
                        }
                    }
                    catch { }
                }
            }
            return 0;
        }

        private static int HandleParseError(IEnumerable<Error> errs)
        {
            foreach (var item in errs)
            {
                Logger.Error(item.ToString());
            }
            return 0;
        }

        #region helpers

        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
                return min;
            if (value.CompareTo(max) > 0)
                return max;

            return value;
        }

        #endregion //helpers
    }
}
