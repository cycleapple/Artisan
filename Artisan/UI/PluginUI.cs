using Artisan.Autocraft;
using Artisan.CraftingLists;
using Artisan.CraftingLogic;
using Artisan.FCWorkshops;
using Artisan.RawInformation;
using Artisan.RawInformation.Character;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using PunishLib.ImGuiMethods;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ThreadLoadImageHandler = ECommons.ImGuiMethods.ThreadLoadImageHandler;
using ECommons.Automation;
using ECommons.WindowsFormsReflector;

namespace Artisan.UI
{
    unsafe internal class PluginUI : Window
    {
        public event EventHandler<bool>? CraftingWindowStateChanged;


        private bool visible = false;
        public OpenWindow OpenWindow { get; set; }

        public bool Visible
        {
            get { return this.visible; }
            set { this.visible = value; }
        }

        private bool settingsVisible = false;
        public bool SettingsVisible
        {
            get { return this.settingsVisible; }
            set { this.settingsVisible = value; }
        }

        private bool craftingVisible = false;
        public bool CraftingVisible
        {
            get { return this.craftingVisible; }
            set { if (this.craftingVisible != value) CraftingWindowStateChanged?.Invoke(this, value); this.craftingVisible = value; }
        }

        public PluginUI() : base($"{P.Name} {P.GetType().Assembly.GetName().Version}###Artisan")
        {
            this.RespectCloseHotkey = false;
            this.SizeConstraints = new()
            {
                MinimumSize = new(250, 100),
                MaximumSize = new(9999, 9999)
            };
            this.TitleBarButtons.Add(new()
            {
                Icon = FontAwesomeIcon.Cog,
                ShowTooltip = () => ImGuiEx.SetTooltip("開啟設定"),
                Click = (x) => P.PluginUi.IsOpen = true,
            });
            P.ws.AddWindow(this);
        }

        public override void PreDraw()
        {
            if (!P.Config.DisableTheme)
            {
                P.Style.Push();
                P.StylePushed = true;
            }

        }

        public override void PostDraw()
        {
            if (P.StylePushed)
            {
                P.Style.Pop();
                P.StylePushed = false;
            }
        }

        public void Dispose()
        {

        }

        public override void Draw()
        {
            if (DalamudInfo.IsOnStaging())
            {
                var scale = ImGui.GetIO().FontGlobalScale;
                ImGui.GetIO().FontGlobalScale = scale * 1.5f;
                using (var f = ImRaii.PushFont(ImGui.GetFont()))
                {
                    ImGuiEx.TextWrapped($"你目前使用的是 Dalamud 測試版；遇到的問題很可能是測試版特有，而非 Artisan 本身造成。Artisan 不保證支援測試版，除非問題也出現在正式版，否則不會針對此類問題修正。");
                    ImGui.Separator();

                    ImGui.Spacing();
                    ImGui.GetIO().FontGlobalScale = scale;
                }

            }
            var region = ImGui.GetContentRegionAvail();
            var itemSpacing = ImGui.GetStyle().ItemSpacing;

            var topLeftSideHeight = region.Y;

            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(5f.Scale(), 0));
            try
            {
                ShowEnduranceMessage();

                using (var table = ImRaii.Table($"ArtisanTableContainer", 2, ImGuiTableFlags.Resizable))
                {
                    if (!table)
                        return;

                    ImGui.TableSetupColumn("##LeftColumn", ImGuiTableColumnFlags.WidthFixed, ImGui.GetWindowWidth() / 2);

                    ImGui.TableNextColumn();

                    var regionSize = ImGui.GetContentRegionAvail();

                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.5f, 0.5f));
                    using (var leftChild = ImRaii.Child($"###ArtisanLeftSide", regionSize with { Y = topLeftSideHeight }, false, ImGuiWindowFlags.NoDecoration))
                    {
                        var imagePath = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/artisan-icon.png");

                        if (ThreadLoadImageHandler.TryGetTextureWrap(imagePath, out var logo))
                        {
                            ImGuiEx.LineCentered("###ArtisanLogo", () =>
                            {
                                ImGui.Image(logo.Handle, new(125f.Scale(), 125f.Scale()));
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.BeginTooltip();
                                    ImGui.Text($"你是第 69 位發現這個秘密的人。真不錯！");
                                    ImGui.EndTooltip();
                                }
                            });

                        }
                        ImGui.Spacing();
                        ImGui.Separator();

                        if (ImGui.Selectable("概覽", OpenWindow == OpenWindow.Overview))
                        {
                            OpenWindow = OpenWindow.Overview;
                        }
                        if (ImGui.Selectable("設定", OpenWindow == OpenWindow.Main))
                        {
                            OpenWindow = OpenWindow.Main;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("耐久製作", OpenWindow == OpenWindow.Endurance))
                        {
                            OpenWindow = OpenWindow.Endurance;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("巨集", OpenWindow == OpenWindow.Macro))
                        {
                            OpenWindow = OpenWindow.Macro;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("Raphael 快取", OpenWindow == OpenWindow.RaphaelCache))
                        {
                            OpenWindow = OpenWindow.RaphaelCache;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("配方批次指定", OpenWindow == OpenWindow.Assigner))
                        {
                            OpenWindow = OpenWindow.Assigner;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("製作清單", OpenWindow == OpenWindow.Lists))
                        {
                            OpenWindow = OpenWindow.Lists;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("清單建立器", OpenWindow == OpenWindow.SpecialList))
                        {
                            OpenWindow = OpenWindow.SpecialList;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("部隊工房", OpenWindow == OpenWindow.FCWorkshop))
                        {
                            OpenWindow = OpenWindow.FCWorkshop;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("模擬器", OpenWindow == OpenWindow.Simulator))
                        {
                            OpenWindow = OpenWindow.Simulator;
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable("關於", OpenWindow == OpenWindow.About))
                        {
                            OpenWindow = OpenWindow.About;
                        }


#if DEBUG
                        drawDebugTab();
#else
                        if(GenericHelpers.IsKeyPressed(Keys.LControlKey) && GenericHelpers.IsKeyPressed(Keys.LShiftKey)) drawDebugTab();
#endif
                        void drawDebugTab()
                        {
                            ImGui.Spacing();
                            if(ImGui.Selectable("DEBUG", OpenWindow == OpenWindow.Debug))
                            {
                                OpenWindow = OpenWindow.Debug;
                            }
                            ImGui.Spacing();
                        }


                    }

                    ImGui.PopStyleVar();
                    ImGui.TableNextColumn();
                    using (var rightChild = ImRaii.Child($"###ArtisanRightSide", Vector2.Zero, false))
                    {
                        switch (OpenWindow)
                        {
                            case OpenWindow.Main:
                                DrawMainWindow();
                                break;
                            case OpenWindow.Endurance:
                                Endurance.Draw();
                                break;
                            case OpenWindow.Lists:
                                CraftingListUI.Draw();
                                break;
                            case OpenWindow.About:
                                AboutTab.Draw("Artisan");
                                break;
                            case OpenWindow.Debug:
                                DebugTab.Draw();
                                break;
                            case OpenWindow.Macro:
                                MacroUI.Draw();
                                break;
                            case OpenWindow.RaphaelCache:
                                RaphaelCacheUI.Draw();
                                break;
                            case OpenWindow.Assigner:
                                AssignerUI.Draw();
                                break;
                            case OpenWindow.FCWorkshop:
                                FCWorkshopUI.Draw();
                                break;
                            case OpenWindow.SpecialList:
                                SpecialLists.Draw();
                                break;
                            case OpenWindow.Overview:
                                DrawOverview();
                                break;
                            case OpenWindow.Simulator:
                                SimulatorUI.Draw();
                                break;
                            case OpenWindow.None:
                                break;
                            default:
                                break;
                        }
                        ;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.Log();
            }
            ImGui.PopStyleVar();
        }

        private void DrawOverview()
        {
            var imagePath = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/artisan.png");

            if (ThreadLoadImageHandler.TryGetTextureWrap(imagePath, out var logo))
            {
                ImGuiEx.LineCentered("###ArtisanTextLogo", () =>
                {
                    ImGui.Image(logo.Handle, new Vector2(logo.Width, 100f.Scale()));
                });
            }

            ImGuiEx.LineCentered("###ArtisanOverview", () =>
            {
                ImGuiEx.TextUnderlined("Artisan－概覽");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"感謝你下載這款製作輔助插件。Artisan 自 2022 年 6 月起持續開發，是作者投入大量心力的作品。");
            ImGui.Spacing();
            ImGuiEx.TextWrapped($"開始使用前，先簡單說明 Artisan 的運作方式。掌握幾個重點後，操作便相當容易。");

            ImGui.Spacing();
            ImGuiEx.LineCentered("###ArtisanModes", () =>
            {
                ImGuiEx.TextUnderlined("製作模式");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"Artisan 的「自動執行技能模式」會替你執行求解器提供的建議技能。" +
                                " 預設會依遊戲允許的最快速度執行，比一般巨集更快。" +
                                " 此功能不會繞過遊戲限制，你也可以自行設定延遲。" +
                                " 啟用自動執行不會改變 Artisan 產生技能建議的方式。");

            var automode = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/AutoMode.png");

            if (ThreadLoadImageHandler.TryGetTextureWrap(automode, out var example))
            {
                ImGuiEx.LineCentered("###AutoModeExample", () =>
                {
                    ImGui.Image(example.Handle, new Vector2(example.Width, example.Height));
                });
            }

            ImGuiEx.TextWrapped($"未啟用自動模式時，還可使用「半手動模式」與「完全手動模式」。" +
                                $" 開始製作後，半手動模式會顯示一個小型浮動視窗。");

            var craftWindowExample = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/ThemeCraftingWindowExample.png");

            if (ThreadLoadImageHandler.TryGetTextureWrap(craftWindowExample, out example))
            {
                ImGuiEx.LineCentered("###CraftWindowExample", () =>
                {
                    ImGui.Image(example.Handle, new Vector2(example.Width, example.Height));
                });
            }

            ImGuiEx.TextWrapped($"點選「執行建議技能」按鈕後，插件會執行目前建議的技能。" +
                $" 由於每一步仍需手動點選，因此屬於半手動模式，但不必在快捷列中尋找技能。" +
                $" 完全手動模式則與平常相同，直接按下快捷列上的技能。" +
                $" Artisan 預設會標示快捷列中的建議技能，亦可在設定中停用此功能。");

            var outlineExample = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/OutlineExample.png");

            if (ThreadLoadImageHandler.TryGetTextureWrap(outlineExample, out example))
            {
                ImGuiEx.LineCentered("###OutlineExample", () =>
                {
                    ImGui.Image(example.Handle, new Vector2(example.Width, example.Height));
                });
            }

            ImGui.Spacing();
            ImGuiEx.LineCentered("###ArtisanSuggestions", () =>
            {
                ImGuiEx.TextUnderlined("求解器／巨集");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"Artisan 預設會建議下一個製作步驟。求解器並非萬能，也無法取代合適的裝備。 " +
                $"只要啟用 Artisan 即可使用此功能，不需要額外設定。 " +
                $"\r\n\r\n" +
                $"若預設求解器無法完成某項製作，可建立 Artisan 巨集，改以巨集內容提供技能建議。 " +
                $"Artisan 巨集沒有長度限制，能依遊戲允許的速度執行，並提供額外的即時調整選項。");

            ImGui.Spacing();
            ImGuiEx.TextUnderlined($"點選此處前往巨集選單。");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (ImGui.IsItemClicked())
            {
                OpenWindow = OpenWindow.Macro;
            }
            ImGui.Spacing();
            ImGuiEx.TextWrapped($"建立巨集後，必須將其指定給配方。可透過配方視窗的下拉選單完成；此視窗預設附加在遊戲製作筆記右上方，也可在設定中解除附加。");


            var recipeWindowExample = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/RecipeWindowExample.png");

            if (ThreadLoadImageHandler.TryGetTextureWrap(recipeWindowExample, out example))
            {
                ImGuiEx.LineCentered("###RecipeWindowExample", () =>
                {
                    ImGui.Image(example.Handle, new Vector2(example.Width, example.Height));
                });
            }


            ImGuiEx.TextWrapped($"從下拉選單選擇已建立的巨集。 " +
                $"製作該道具時，技能建議便會改為巨集內容。");


            ImGui.Spacing();
            ImGuiEx.LineCentered("###Endurance", () =>
            {
                ImGuiEx.TextUnderlined("耐久製作");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"Artisan 的「耐久製作模式」相當於自動重複製作，會持續嘗試製作同一項道具。 " +
                $"先在遊戲製作筆記選擇配方，再啟用此功能即可。 " +
                $"角色會在素材足夠的情況下持續製作該道具。 " +
                $"\r\n\r\n" +
                $"耐久製作也能在每次製作之間管理食物、藥品、指南、修理與精製魔晶石。 " +
                $"修理功能僅支援使用暗物質自行修理，不支援修理工。");

            ImGui.Spacing();
            ImGuiEx.TextUnderlined($"點選此處前往耐久製作選單。");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (ImGui.IsItemClicked())
            {
                OpenWindow = OpenWindow.Endurance;
            }

            ImGui.Spacing();
            ImGuiEx.LineCentered("###Lists", () =>
            {
                ImGuiEx.TextUnderlined("製作清單");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"Artisan 可建立道具清單，並依序自動製作其中每個項目。 " +
                $"製作清單提供多種工具，能簡化從素材到成品的流程。 " +
                $"同時支援與 Teamcraft 互相匯入及匯出。");

            ImGui.Spacing();
            ImGuiEx.TextUnderlined($"點選此處前往製作清單選單。");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (ImGui.IsItemClicked())
            {
                OpenWindow = OpenWindow.Lists;
            }

            ImGui.Spacing();
            ImGuiEx.LineCentered("###Questions", () =>
            {
                ImGuiEx.TextUnderlined("有疑問嗎？");
            });
            ImGui.Spacing();

            ImGuiEx.TextWrapped($"若有此處未說明的疑問，可前往我們的");
            ImGui.SameLine(ImGui.GetCursorPosX(), 1.5f);
            ImGuiEx.TextUnderlined($"Discord 伺服器提問。");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsItemClicked())
                {
                    Util.OpenLink("https://discord.gg/Zzrcc8kmvy");
                }
            }

            ImGuiEx.TextWrapped($"也可以在我們的");
            ImGui.SameLine(ImGui.GetCursorPosX(), 2f);
            ImGuiEx.TextUnderlined($"GitHub 頁面回報問題。");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsItemClicked())
                {
                    Util.OpenLink("https://github.com/PunishXIV/Artisan");
                }
            }

        }

        public static void DrawMainWindow()
        {
            ImGui.TextWrapped($"此處可調整 Artisan 使用的設定，其中部分選項也能在製作期間切換。");
            ImGui.TextWrapped($"若要使用 Artisan 的手動技能標示，請將所有已解鎖的製作技能放在可見的快捷列上。");
            bool autoEnabled = P.Config.AutoMode;
            bool delayRec = P.Config.DelayRecommendation;
            bool failureCheck = P.Config.DisableFailurePrediction;
            int maxQuality = P.Config.MaxPercentage;
            bool useTricksGood = P.Config.UseTricksGood;
            bool useTricksExcellent = P.Config.UseTricksExcellent;
            bool useSpecialist = P.Config.UseSpecialist;
            //bool showEHQ = P.Config.ShowEHQ;
            //bool useSimulated = P.Config.UseSimulatedStartingQuality;
            bool disableGlow = P.Config.DisableHighlightedAction;
            bool disableToasts = P.Config.DisableToasts;

            ImGui.Separator();

            if (ImGui.CollapsingHeader("一般設定"))
            {
                if (ImGui.Checkbox("自動執行技能模式", ref autoEnabled))
                {
                    P.Config.AutoMode = autoEnabled;
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker($"自動使用每一個建議技能。");
                if (autoEnabled)
                {
                    if (ImGui.Checkbox($"模擬遊戲巨集延遲", ref P.Config.ReplicateMacroDelay))
                    {
                        P.Config.Save();
                    }

                    if (ImGui.Checkbox("附近有其他玩家時模擬遊戲巨集延遲", ref P.Config.ReplicateMacroDelayWhenPlayersNearby))
                    {
                        P.Config.Save();
                    }
                    ImGuiComponents.HelpMarker("偵測到其他已載入的玩家角色時，暫時改用遊戲巨集延遲；否則維持自訂延遲。");

                    if (!P.Config.ReplicateMacroDelay)
                    {
                        var delay = P.Config.AutoDelay;
                        ImGui.PushItemWidth(200);
                        if (ImGui.SliderInt("執行延遲（毫秒）###ActionDelay", ref delay, 0, 1000))
                        {
                            if (delay < 0) delay = 0;
                            if (delay > 1000) delay = 1000;

                            P.Config.AutoDelay = delay;
                            P.Config.Save();
                        }
                    }
                }

                bool requireFoodPot = P.Config.AbortIfNoFoodPot;
                if (ImGui.Checkbox("強制使用消耗品", ref requireFoodPot))
                {
                    P.Config.AbortIfNoFoodPot = requireFoodPot;
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker("Artisan 會要求使用已設定的食物、指南或藥品；找不到所需消耗品時將拒絕開始製作。");

                if (ImGui.Checkbox("製作練習時使用消耗品", ref P.Config.UseConsumablesTrial))
                {
                    P.Config.Save();
                }

                if (ImGui.Checkbox("簡易製作時使用消耗品", ref P.Config.UseConsumablesQuickSynth))
                {
                    P.Config.Save();
                }

                ImGui.Indent();
                if (ImGui.CollapsingHeader("預設消耗品"))
                {
                    bool changed = false;
                    changed |= P.Config.DefaultConsumables.DrawFood();
                    changed |= P.Config.DefaultConsumables.DrawPotion();
                    changed |= P.Config.DefaultConsumables.DrawManual();
                    changed |= P.Config.DefaultConsumables.DrawSquadronManual();

                    if (changed)
                    {
                        P.Config.Save();
                    }
                }
                ImGui.Unindent();

                if (ImGui.Checkbox($"優先交由修理工修理", ref P.Config.PrioritizeRepairNPC))
                {
                    P.Config.Save();
                }

                ImGuiComponents.HelpMarker("修理時若附近有修理工，會優先交由修理工處理。找不到修理工且職業等級符合需求時，仍會嘗試自行修理。");

                if (ImGui.Checkbox($"無法修理時停用耐久製作", ref P.Config.DisableEnduranceNoRepair))
                    P.Config.Save();

                ImGuiComponents.HelpMarker($"達到修理門檻後，若無法自行修理或交由修理工處理，則停用耐久製作。");

                if (ImGui.Checkbox($"無法修理時暫停製作清單", ref P.Config.DisableListsNoRepair))
                    P.Config.Save();

                ImGuiComponents.HelpMarker($"達到修理門檻後，若無法自行修理或交由修理工處理，則暫停目前的製作清單。");

                bool requestStop = P.Config.RequestToStopDuty;
                bool requestResume = P.Config.RequestToResumeDuty;
                int resumeDelay = P.Config.RequestToResumeDelay;

                if (ImGui.Checkbox("任務搜尋器準備完成時停用耐久製作／暫停清單", ref requestStop))
                {
                    P.Config.RequestToStopDuty = requestStop;
                    P.Config.Save();
                }

                if (requestStop)
                {
                    if (ImGui.Checkbox("離開任務後恢復耐久製作／繼續清單", ref requestResume))
                    {
                        P.Config.RequestToResumeDuty = requestResume;
                        P.Config.Save();
                    }

                    if (requestResume)
                    {
                        if (ImGui.SliderInt("恢復前延遲（秒）", ref resumeDelay, 5, 60))
                        {
                            P.Config.RequestToResumeDelay = resumeDelay;
                        }
                    }
                }

                if (ImGui.Checkbox("停用自動裝備製作所需道具", ref P.Config.DontEquipItems))
                    P.Config.Save();

                if (ImGui.Checkbox("耐久製作完成後播放音效", ref P.Config.PlaySoundFinishEndurance))
                    P.Config.Save();
                
                if (ImGui.Checkbox("製作發生錯誤後播放音效", ref P.Config.PlaySoundError))
                    P.Config.Save();

                if (ImGui.Checkbox($"製作清單完成後播放音效", ref P.Config.PlaySoundFinishList))
                    P.Config.Save();

                if (P.Config.PlaySoundFinishEndurance || P.Config.PlaySoundFinishList || P.Config.PlaySoundError)
                {
                    if (ImGui.SliderFloat("音效音量", ref P.Config.SoundVolume, 0f, 1f, "%.2f"))
                        P.Config.Save();
                }

                if (ImGuiEx.ButtonCtrl("重設宇宙探索製作設定"))
                {
                    var copy = P.Config.RecipeConfigs;
                    foreach (var c in copy)
                    {
                        if (Svc.Data.GetExcelSheet<Recipe>().GetRow(c.Key).Number == 0)
                            P.Config.RecipeConfigs.Remove(c.Key);
                    }
                }
            }
            if (ImGui.CollapsingHeader("巨集設定"))
            {
                if (ImGui.Checkbox("無法使用技能時略過該巨集步驟", ref P.Config.SkipMacroStepIfUnable))
                    P.Config.Save();

                if (ImGui.Checkbox($"巨集結束後不再由 Artisan 繼續製作", ref P.Config.DisableMacroArtisanRecommendation))
                    P.Config.Save();
            }
            if (ImGui.CollapsingHeader("標準配方求解器設定"))
            {
                if (ImGui.Checkbox($"使用 {Skills.TricksOfTrade.NameOfAction()}－{LuminaSheets.AddonSheet[227].Text}", ref useTricksGood))
                {
                    P.Config.UseTricksGood = useTricksGood;
                    P.Config.Save();
                }
                ImGui.SameLine();
                if (ImGui.Checkbox($"使用 {Skills.TricksOfTrade.NameOfAction()}－{LuminaSheets.AddonSheet[228].Text}", ref useTricksExcellent))
                {
                    P.Config.UseTricksExcellent = useTricksExcellent;
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker($"這兩個選項會在狀態為 {LuminaSheets.AddonSheet[227].Text} 或 {LuminaSheets.AddonSheet[228].Text} 時優先使用 {Skills.TricksOfTrade.NameOfAction()}。\n\n這將取代 {Skills.PreciseTouch.NameOfAction()} 與 {Skills.IntensiveSynthesis.NameOfAction()} 的使用。\n\n尚未習得上述技能或符合特定情況時，無論設定為何仍可能使用 {Skills.TricksOfTrade.NameOfAction()}。");
                if (ImGui.Checkbox("使用專家技能", ref useSpecialist))
                {
                    P.Config.UseSpecialist = useSpecialist;
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker("目前職業為專家時，會消耗持有的能工巧匠圖紙。\n使用設計變動取代觀察。\n製作前期會使用專心致志以便施展集中加工。");
                ImGui.TextWrapped("最高品質百分比");
                ImGuiComponents.HelpMarker($"品質達到下方設定的百分比後，Artisan 只會推進作業進度。");
                if (ImGui.SliderInt("###SliderMaxQuality", ref maxQuality, 0, 100, $"%d%%"))
                {
                    P.Config.MaxPercentage = maxQuality;
                    P.Config.Save();
                }

                ImGui.Text($"收藏品門檻");
                ImGuiComponents.HelpMarker("收藏品達到指定門檻後，求解器將停止提高品質。");

                if (ImGui.RadioButton($"最低", P.Config.SolverCollectibleMode == 1))
                {
                    P.Config.SolverCollectibleMode = 1;
                    P.Config.Save();
                }
                ImGui.SameLine();
                if (ImGui.RadioButton($"中等", P.Config.SolverCollectibleMode == 2))
                {
                    P.Config.SolverCollectibleMode = 2;
                    P.Config.Save();
                }
                ImGui.SameLine();
                if (ImGui.RadioButton($"最高", P.Config.SolverCollectibleMode == 3))
                {
                    P.Config.SolverCollectibleMode = 3;
                    P.Config.Save();
                }

                if (ImGui.Checkbox($"使用品質起手式（{Skills.Reflect.NameOfAction()}）", ref P.Config.UseQualityStarter))
                    P.Config.Save();
                ImGuiComponents.HelpMarker($"耐久較低的配方通常較適合此選項。");

                //if (ImGui.Checkbox("Low Stat Mode", ref P.Config.LowStatsMode))
                //    P.Config.Save();

                //ImGuiComponents.HelpMarker("This swaps out Waste Not II & Groundwork for Prudent Synthesis");

                ImGui.TextWrapped($"{Skills.PreparatoryTouch.NameOfAction()}－{Buffs.InnerQuiet.NameOfBuff()}最高層數");
                ImGui.SameLine();
                ImGuiComponents.HelpMarker($"只會使用 {Skills.PreparatoryTouch.NameOfAction()}，直到 {Buffs.InnerQuiet.NameOfBuff()} 達到設定層數。可藉此調整 CP 消耗。");
                if (ImGui.SliderInt($"###MaxIQStacksPrepTouch", ref P.Config.MaxIQPrepTouch, 0, 10))
                    P.Config.Save();

                if (ImGui.Checkbox($"可用時使用比爾格的奇蹟", ref P.Config.UseMaterialMiracle))
                    P.Config.Save();
                ImGuiComponents.HelpMarker($"增益效果持續期間會由標準配方求解器切換至專家配方求解器。由於這是限時效果而非永久層數效果，模擬器無法正確模擬，因此結果可能不準確。");
				ImGui.PushItemWidth(250);
				if (ImGui.SliderInt($"嘗試使用比爾格的奇蹟前至少執行的步數###P.Config.MinimumStepsBeforeMiracle", ref P.Config.MinimumStepsBeforeMiracle, 0, 20))
					P.Config.Save();

                if (P.Config.UseMaterialMiracle)
                {
                    ImGui.Indent();
                    if (ImGui.Checkbox($"每次製作允許使用多次", ref P.Config.MaterialMiracleMulti))
                        P.Config.Save();

                    ImGui.Unindent();
                }

            }
            bool openExpert = false;
            if (ImGui.CollapsingHeader("專家配方求解器設定"))
            {
                openExpert = true;
                if (P.Config.ExpertSolverConfig.expertIcon is not null)
                {
                    ImGui.SameLine();
                    ImGui.Image(P.Config.ExpertSolverConfig.expertIcon.Handle, new(P.Config.ExpertSolverConfig.expertIcon.Width * ImGuiHelpers.GlobalScaleSafe, ImGui.GetItemRectSize().Y), new(0, 0), new Vector2(1, 1), new(0.94f, 0.57f, 0f, 1f));
                }
                if (P.Config.ExpertSolverConfig.Draw())
                    P.Config.Save();
            }
            if (!openExpert)
            {
                if (P.Config.ExpertSolverConfig.expertIcon is not null)
                {
                    ImGui.SameLine();
                    ImGui.Image(P.Config.ExpertSolverConfig.expertIcon.Handle, new(P.Config.ExpertSolverConfig.expertIcon.Width * ImGuiHelpers.GlobalScaleSafe, ImGui.GetItemRectSize().Y), new(0, 0), new Vector2(1, 1), new(0.94f, 0.57f, 0f, 1f));
                }
            }

            if (ImGui.CollapsingHeader("Raphael 求解器設定"))
            {
                if (P.Config.RaphaelSolverConfig.Draw())
                    P.Config.Save();
            }

            using (ImRaii.Disabled())
            {
                if (ImGui.CollapsingHeader("腳本求解器設定（目前停用）"))
                {
                    if (P.Config.ScriptSolverConfig.Draw())
                        P.Config.Save();
                }
            }
            if (ImGui.CollapsingHeader("介面設定"))
            {
                if (ImGui.Checkbox("停用技能標示框", ref disableGlow))
                {
                    P.Config.DisableHighlightedAction = disableGlow;
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker("手動操作時，此方框會在快捷列上標示建議技能。");

                if (ImGui.Checkbox($"停用技能建議通知", ref disableToasts))
                {
                    P.Config.DisableToasts = disableToasts;
                    P.Config.Save();
                }

                ImGuiComponents.HelpMarker("每次出現新技能建議時顯示的彈出通知。");

                bool lockMini = P.Config.LockMiniMenuR;
                if (ImGui.Checkbox("讓製作筆記迷你選單保持附加於製作筆記", ref lockMini))
                {
                    P.Config.LockMiniMenuR = lockMini;
                    P.Config.Save();
                }

                if (!P.Config.LockMiniMenuR)
                {
                    if (ImGui.Checkbox($"固定迷你選單位置", ref P.Config.PinMiniMenu))
                    {
                        P.Config.Save();
                    }
                }

                if (ImGui.Button("重設製作筆記迷你選單位置"))
                {
                    AtkResNodeFunctions.ResetPosition = true;
                }

                if (ImGui.Checkbox($"擴充搜尋列功能", ref P.Config.ReplaceSearch))
                {
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker($"擴充製作筆記的搜尋列，提供即時結果，並可點選開啟配方。");

                bool hideQuestHelper = P.Config.HideQuestHelper;
                if (ImGui.Checkbox($"隱藏任務助手", ref hideQuestHelper))
                {
                    P.Config.HideQuestHelper = hideQuestHelper;
                    P.Config.Save();
                }

                bool hideTheme = P.Config.DisableTheme;
                if (ImGui.Checkbox("停用自訂主題", ref hideTheme))
                {
                    P.Config.DisableTheme = hideTheme;
                    P.Config.Save();
                }
                ImGui.SameLine();

                if (IconButtons.IconTextButton(FontAwesomeIcon.Clipboard, "複製主題"))
                {
                    ImGui.SetClipboardText("DS1H4sIAAAAAAAACq1YS3PbNhD+Kx2ePR6AeJG+xXYbH+KOJ3bHbW60REusaFGlKOXhyX/v4rEACEqumlY+ECD32/cuFn7NquyCnpOz7Cm7eM1+zy5yvfnDPL+fZTP4at7MHVntyMi5MGTwBLJn+HqWLZB46Ygbx64C5kQv/nRo8xXQ3AhZZRdCv2jdhxdHxUeqrJO3Ftslb5l5u/Fa2rfEvP0LWBkBPQiSerF1Cg7wApBn2c5wOMv2juNn9/zieH09aP63g+Kqyr1mI91mHdj5mj3UX4bEG+b5yT0fzRPoNeF1s62e2np+EuCxWc+7z5cLr1SuuCBlkTvdqBCEKmaQxCHJeZmXnFKlgMHVsmnnEZ5IyXMiFUfjwt6yCHvDSitx1212m4gHV0QURY4saMEYl6Q4rsRl18/rPuCZQ+rFJxeARwyAJb5fVmD4NBaJEK3eL331UscuAgflOcY0J5zLUioHpHmhCC0lCuSBwU23r3sfF/0N0wKdoxcGFqHezYZmHypJIkgiSCJIalc8NEM7Utb6ErWlwngt9aUoFRWSB3wilRUl5SRwISUFvhJt9lvDrMgLIjgLzK66tq0228j0H+R3W693l1UfmUd9kqA79MKn9/2sB9lPI8hbofb073vdh1BbQYRgqKzfGbTfTWVqHmnMOcXUpI6BXhzGJjEQCNULmy4x9GpZz1a3Vb8KqaIDz4RPVGZin6dlZPKDSS29baAyRqYfzVGnr0ekaaowTbEw9MLjLnfD0GGT1unHSSlKr2lRyqLA2qU5ESovi6m+lkvqYiZ1/ygxyqrgjDKF8Yr2lp1pd4R7dokhvOBUQk37TCVKQbX4TMVtyuymruKWJCURVEofClYWbNpWCQfFifDwsWnYyXXS8ZxDOI+H0uLToPzrhKg3VV8N3amt1dP/t5goW/E85pg2pB8N8sd623yr3/dNOPYVstELg9cLA8zFCJKapQpEYkPVi9CMA/L/Uv8hrk1hmg9WKKMQXyIxnGFrm6i06MkhBHlIiQ8rI0xx4k/rsLWBsWpbTmmhqFIypcvUHTRgQ859V/bbKaPf1s/dbBcfD0R6NnCWwg/dS3lB4MfQMSrnCY9EK8qEw9uUl4YdHjRQRVFTuu5mq2a9uOvrfVOH0SDHqtXxMjDfi1RA/fyyGb7G5y5KdJg8EnTXdsOHZl1vQyJJQrlCQTDsEBi80HdhO+VwrEP48hwdTRp202yHbgGzhRfu03/UCA4gjglDd44mUT2D2i4UH9coSy8mfjEYN54NfbcOOIZnn15M7YqAH5rFEmdl3eJ8r0N5E9zH0fz71nQQyN+1/zSP6yR2A/l93dazoY6n5DdyiumWc91Xi+u+2zxU/aI+Jipq2QD5tdrfgO3t2P5jcqz9gLEXAEjgFHzcMJUgr5uXyDQsNSxZtCvX81s3r1qLOw0EztC3ORiEs4vssu9W9fqn2263HqpmncFF016PqklGjh1kjQ2NUyUJH08mcIk9gSrqn+jg0XFoqeqTrmDPwQv+PDEr6wl3oljaxcRSRTCyMc/lJJ/lAcnNhMr3WWZ+ES3exrXE+HJ2yNOrowkb97A2cExdXcrYjaFToVDfGSMqnCaDa0pi/vzNMyLG/wQEyzmzfhx7KAwJUn93Fz6v5shD8B+DRAG4Oh+QHYapovAd3/OEQzuiDSdE4c8wjJHh7iiBFFozvP3+NxT8RWGlEQAA");
                    Notify.Success("已將主題複製到剪貼簿。");
                }

                if (ImGui.Checkbox("停用製作清單的 Allagan Tools 整合", ref P.Config.DisableAllaganTools))
                    P.Config.Save();

                if (ImGui.Checkbox("停用 Artisan 右鍵選單選項", ref P.Config.HideContextMenus))
                    P.Config.Save();

                ImGuiComponents.HelpMarker("在製作筆記中的配方上按右鍵或方塊鍵時，Artisan 會新增選項。");

                ImGui.Indent();
                if (ImGui.CollapsingHeader("模擬器設定"))
                {
                    if (ImGui.Checkbox("隱藏配方視窗中的模擬器結果", ref P.Config.HideRecipeWindowSimulator))
                        P.Config.Save();

                    if (ImGui.SliderFloat("模擬器技能圖示大小", ref P.Config.SimulatorActionSize, 5f, 70f))
                    {
                        P.Config.Save();
                    }
                    ImGuiComponents.HelpMarker("設定模擬器分頁內技能圖示的大小。");

                    if (ImGui.Checkbox("啟用手動模式滑鼠懸停預覽", ref P.Config.SimulatorHoverMode))
                        P.Config.Save();

                    if (ImGui.Checkbox($"隱藏技能說明", ref P.Config.DisableSimulatorActionTooltips))
                        P.Config.Save();

                    ImGuiComponents.HelpMarker("手動模式中將滑鼠移到技能上時，不顯示技能說明。");
                }
                ImGui.Unindent();
            }
            if (ImGui.CollapsingHeader("製作清單設定"))
            {
                ImGui.TextWrapped($"建立製作清單時會自動套用下列設定。");

                if (ImGui.Checkbox("略過持有數量已足夠的項目", ref P.Config.DefaultListSkip))
                {
                    P.Config.Save();
                }

                if (ImGui.Checkbox("自動精製魔晶石", ref P.Config.DefaultListMateria))
                {
                    P.Config.Save();
                }

                if (ImGui.Checkbox("自動修理", ref P.Config.DefaultListRepair))
                {
                    P.Config.Save();
                }

                if (P.Config.DefaultListRepair)
                {
                    ImGui.TextWrapped($"耐久度低於此值時修理");
                    ImGui.SameLine();
                    if (ImGui.SliderInt("###SliderRepairDefault", ref P.Config.DefaultListRepairPercent, 0, 100, $"%d%%"))
                    {
                        P.Config.Save();
                    }
                }

                if (ImGui.Checkbox("新加入清單的項目預設使用簡易製作", ref P.Config.DefaultListQuickSynth))
                {
                    P.Config.Save();
                }

                if (ImGui.Checkbox($@"加入清單後重設「加入的製作次數」", ref P.Config.ResetTimesToAdd))
                    P.Config.Save();

                ImGui.PushItemWidth(100);
                if (ImGui.InputInt("從右鍵選單加入的製作次數", ref P.Config.ContextMenuLoops))
                {
                    if (P.Config.ContextMenuLoops <= 0)
                        P.Config.ContextMenuLoops = 1;

                    P.Config.Save();
                }

                ImGui.PushItemWidth(400);
                if (ImGui.SliderFloat("每次製作之間的延遲", ref P.Config.ListCraftThrottle2, 0f, 2f, "%.1f"))
                {
                    if (P.Config.ListCraftThrottle2 < 0f)
                        P.Config.ListCraftThrottle2 = 0f;

                    if (P.Config.ListCraftThrottle2 > 2f)
                        P.Config.ListCraftThrottle2 = 2f;

                    P.Config.Save();
                }

                ImGui.Indent();
                if (ImGui.CollapsingHeader("素材表格設定"))
                {
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, $"若已查看過某份清單的素材表格，下列預設欄位設定不會套用至該清單。");

                    if (ImGui.Checkbox($@"預設隱藏「物品欄」欄位", ref P.Config.DefaultHideInventoryColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「雇員」欄位", ref P.Config.DefaultHideRetainerColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「尚缺數量」欄位", ref P.Config.DefaultHideRemainingColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「來源」欄位", ref P.Config.DefaultHideCraftableColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「可製作數量」欄位", ref P.Config.DefaultHideCraftableCountColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「用於製作」欄位", ref P.Config.DefaultHideCraftItemsColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「分類」欄位", ref P.Config.DefaultHideCategoryColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「採集區域」欄位", ref P.Config.DefaultHideGatherLocationColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設隱藏「ID」欄位", ref P.Config.DefaultHideIdColumn))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設啟用「只顯示 HQ 製作素材」", ref P.Config.DefaultHQCrafts))
                        P.Config.Save();

                    if (ImGui.Checkbox($"預設啟用「顏色檢查」", ref P.Config.DefaultColourValidation))
                        P.Config.Save();

                    if (ImGui.Checkbox($"從 Universalis 取得價格", ref P.Config.UseUniversalis))
                        P.Config.Save();

                    if (P.Config.UseUniversalis)
                    {
                        if (ImGui.Checkbox($"Universalis 僅查詢目前資料中心", ref P.Config.LimitUnversalisToDC))
                            P.Config.Save();

                        if (ImGui.Checkbox($"僅在要求時取得價格", ref P.Config.UniversalisOnDemand))
                            P.Config.Save();

                        ImGuiComponents.HelpMarker("必須逐項點選按鈕才能取得價格。");
                    }
                }

                ImGui.Unindent();
            }
        }

        private void ShowEnduranceMessage()
        {
            if (!P.Config.ViewedEnduranceMessage)
            {
                P.Config.ViewedEnduranceMessage = true;
                P.Config.Save();

                ImGui.OpenPopup("EndurancePopup");

                var windowSize = new Vector2(512 * ImGuiHelpers.GlobalScale,
                    ImGui.GetTextLineHeightWithSpacing() * 13 + 2 * ImGui.GetFrameHeightWithSpacing() * 2f);
                ImGui.SetNextWindowSize(windowSize);
                ImGui.SetNextWindowPos((ImGui.GetIO().DisplaySize - windowSize) / 2);

                using var popup = ImRaii.Popup("EndurancePopup",
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.Modal);
                if (!popup)
                    return;

                ImGui.TextWrapped($@"近期有不少使用者回報耐久製作不再設定素材。自先前版本起，舊有的耐久製作行為已移至新的設定選項。");
                ImGui.Dummy(new Vector2(0));

                var imagePath = Path.Combine(Svc.PluginInterface.AssemblyLocation.DirectoryName!, "Images/EnduranceNewSetting.png");

                if (ThreadLoadImageHandler.TryGetTextureWrap(imagePath, out var img))
                {
                    ImGuiEx.LineCentered("###EnduranceNewSetting", () =>
                    {
                        ImGui.Image(img.Handle, new Vector2(img.Width, img.Height));
                    });
                }

                ImGui.Spacing();

                ImGui.TextWrapped($"此變更是為了恢復耐久製作最初的行為。若不在意素材比例，請務必啟用最大數量模式。");

                ImGui.SetCursorPosY(windowSize.Y - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y);
                if (ImGui.Button("關閉", -Vector2.UnitX))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    public enum OpenWindow
    {
        None = 0,
        Main = 1,
        Endurance = 2,
        Macro = 3,
        Lists = 4,
        About = 5,
        Debug = 6,
        FCWorkshop = 7,
        SpecialList = 8,
        Overview = 9,
        Simulator = 10,
        RaphaelCache = 11,
        Assigner = 12,
    }
}
