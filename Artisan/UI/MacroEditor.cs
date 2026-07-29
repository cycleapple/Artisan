using Artisan.CraftingLogic;
using Artisan.CraftingLogic.Solvers;
using Artisan.GameInterop;
using Artisan.RawInformation;
using Artisan.RawInformation.Character;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Dalamud.Bindings.ImGui;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Numerics;

namespace Artisan.UI
{
    internal class MacroEditor : Window
    {
        private MacroSolverSettings.Macro SelectedMacro;
        private bool renameMode = false;
        private string renameMacro = "";
        private int selectedStepIndex = -1;
        private bool Raweditor = false;
        private static string _rawMacro = string.Empty;
        private bool raphael_cache = false;

        public MacroEditor(MacroSolverSettings.Macro macro, bool raphael_cache = false) : base($"巨集編輯器###{macro.ID}", ImGuiWindowFlags.None)
        {
            this.raphael_cache = raphael_cache;
            SelectedMacro = macro;
            selectedStepIndex = macro.Steps.Count - 1;
            this.IsOpen = true;
            P.ws.AddWindow(this);
            this.Size = new Vector2(600, 600);
            this.SizeCondition = ImGuiCond.Appearing;
            ShowCloseButton = true;

            Crafting.CraftStarted += OnCraftStarted;
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

        public override void OnClose()
        {
            Crafting.CraftStarted -= OnCraftStarted;
            base.OnClose();
            P.ws.RemoveWindow(this);
        }

        public override void Draw()
        {
            if (SelectedMacro.ID != 0)
            {
                if (!renameMode)
                {
                    ImGui.TextUnformatted($"目前巨集：{SelectedMacro.Name}");
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Pen))
                    {
                        renameMode = true;
                    }
                }
                else
                {
                    renameMacro = SelectedMacro.Name!;
                    if (ImGui.InputText("", ref renameMacro, 64, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        SelectedMacro.Name = renameMacro;
                        P.Config.Save();

                        renameMode = false;
                        renameMacro = String.Empty;
                    }
                }
                if (ImGui.Button("刪除巨集（按住 Ctrl）") && ImGui.GetIO().KeyCtrl)
                {
                    if (raphael_cache)
                    {
                        var copy = P.Config.RaphaelSolverCacheV3.Where(kv => kv.Value == SelectedMacro);
                        //really should be just one but is it for sure??
                        foreach (var kv in copy)
                        {
                            P.Config.RaphaelSolverCacheV3.TryRemove(kv);
                        }
                    }
                    else
                    {
                        P.Config.MacroSolverConfig.Macros.Remove(SelectedMacro);
                        foreach (var e in P.Config.RecipeConfigs)
                            if (e.Value.SolverType == typeof(MacroSolverDefinition).FullName && e.Value.SolverFlavour == SelectedMacro.ID)
                                P.Config.RecipeConfigs.Remove(e.Key); // TODO: do we want to preserve other configs?..
                    }
                    P.Config.Save();
                    SelectedMacro = new();
                    selectedStepIndex = -1;

                    this.IsOpen = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("原始文字編輯器"))
                {
                    _rawMacro = string.Join("\r\n", SelectedMacro.Steps.Select(x => $"{x.Action.NameOfAction()}"));
                    Raweditor = !Raweditor;
                }

                ImGui.SameLine();
                var exportButton = ImGuiHelpers.GetButtonSize("匯出巨集");
                ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - exportButton.X);

                if (ImGui.Button("匯出巨集###ExportButton"))
                {
                    ImGui.SetClipboardText(JsonConvert.SerializeObject(SelectedMacro));
                    Notify.Success("已將巨集複製到剪貼簿。");
                }

                ImGui.Spacing();
                if (ImGui.Checkbox("品質達到 100% 時略過品質技能", ref SelectedMacro.Options.SkipQualityIfMet))
                {
                    P.Config.Save();
                }
                ImGuiComponents.HelpMarker("品質達到 100% 後，巨集會略過所有與品質有關的技能，包括增益技能。");
                ImGui.SameLine();
                if (ImGui.Checkbox("非低品質時略過「觀察」", ref SelectedMacro.Options.SkipObservesIfNotPoor))
                {
                    P.Config.Save();
                }


                if (ImGui.Checkbox("升級品質技能", ref SelectedMacro.Options.UpgradeQualityActions))
                    P.Config.Save();
                ImGuiComponents.HelpMarker("若狀態為高品質或最高品質，且巨集目前執行的是提升品質的技能（不包含比爾格的祝福），則會將該技能替換為集中加工。");
                ImGui.SameLine();

                if (ImGui.Checkbox("升級作業進度技能", ref SelectedMacro.Options.UpgradeProgressActions))
                    P.Config.Save();
                ImGuiComponents.HelpMarker("若狀態為高品質或最高品質，且巨集目前執行的是提升作業進度的技能，則會將該技能替換為集中製作。");

                ImGui.PushItemWidth(150f);
                if (ImGui.InputInt("最低作業精度", ref SelectedMacro.Options.MinCraftsmanship))
                    P.Config.Save();
                ImGuiComponents.HelpMarker("選用此巨集時，若作業精度未達門檻，Artisan 將不會開始製作。");

                ImGui.PushItemWidth(150f);
                if (ImGui.InputInt("最低加工精度", ref SelectedMacro.Options.MinControl))
                    P.Config.Save();
                ImGuiComponents.HelpMarker("選用此巨集時，若加工精度未達門檻，Artisan 將不會開始製作。");

                ImGui.PushItemWidth(150f);
                if (ImGui.InputInt("最低 CP", ref SelectedMacro.Options.MinCP))
                    P.Config.Save();
                ImGuiComponents.HelpMarker("選用此巨集時，若 CP 未達門檻，Artisan 將不會開始製作。");

                if (!Raweditor)
                {
                    if (ImGui.Button($"插入新技能（{Skills.BasicSynthesis.NameOfAction()}）"))
                    {
                        SelectedMacro.Steps.Insert(selectedStepIndex + 1, new() { Action = Skills.BasicSynthesis });
                        ++selectedStepIndex;
                        P.Config.Save();
                    }

                    if (selectedStepIndex >= 0)
                    {
                        if (ImGui.Button($"插入與上一個相同的技能（{SelectedMacro.Steps[selectedStepIndex].Action.NameOfAction()}）"))
                        {
                            SelectedMacro.Steps.Insert(selectedStepIndex + 1, new() { Action = SelectedMacro.Steps[selectedStepIndex].Action });
                            ++selectedStepIndex;
                            P.Config.Save();
                        }
                    }


                    ImGui.Columns(2, "actionColumns", true);
                    ImGui.SetColumnWidth(0, 220f.Scale());
                    ImGuiEx.LineCentered("###MacroActions", () => ImGuiEx.TextUnderlined("巨集技能"));
                    ImGui.Indent();
                    for (int i = 0; i < SelectedMacro.Steps.Count; i++)
                    {
                        var step = SelectedMacro.Steps[i];
                        var selectedAction = ImGui.Selectable($"{i + 1}. {(step.Action == Skills.None ? "Artisan 建議" : step.Action.NameOfAction())}{(step.HasExcludeCondition ? " | " : "")}{(step.HasExcludeCondition && step.ReplaceOnExclude ? step.ReplacementAction.NameOfAction() : step.HasExcludeCondition ? "略過" : "")}###selectedAction{i}", i == selectedStepIndex);
                        if (selectedAction)
                            selectedStepIndex = i;
                    }
                    ImGui.Unindent();
                    if (selectedStepIndex >= 0)
                    {
                        var step = SelectedMacro.Steps[selectedStepIndex];

                        ImGui.NextColumn();
                        ImGuiEx.CenterColumnText($"目前技能：{(step.Action == Skills.None ? "Artisan 建議" : step.Action.NameOfAction())}", true);
                        if (selectedStepIndex > 0)
                        {
                            ImGui.SameLine();
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft))
                            {
                                selectedStepIndex--;
                            }
                        }

                        if (selectedStepIndex < SelectedMacro.Steps.Count - 1)
                        {
                            ImGui.SameLine();
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowRight))
                            {
                                selectedStepIndex++;
                            }
                        }

                        ImGui.Dummy(new Vector2(0, 0));
                        ImGui.SameLine();
                        if (ImGui.Checkbox($"此技能不套用升級", ref step.ExcludeFromUpgrade))
                            P.Config.Save();

                        ImGui.Spacing();
                        ImGuiEx.CenterColumnText($"遇到下列狀態時略過", true);

                        ImGui.BeginChild("ConditionalExcludes", new Vector2(ImGui.GetContentRegionAvail().X, step.HasExcludeCondition ? 200f : 100f), false, ImGuiWindowFlags.AlwaysAutoResize);
                        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, 0));
                        ImGui.Columns(3, border: false);
                        if (ImGui.Checkbox($"通常", ref step.ExcludeNormal))
                            P.Config.Save();
                        if (ImGui.Checkbox($"低品質", ref step.ExcludePoor))
                            P.Config.Save();
                        if (ImGui.Checkbox($"高品質", ref step.ExcludeGood))
                            P.Config.Save();
                        if (ImGui.Checkbox($"最高品質", ref step.ExcludeExcellent))
                            P.Config.Save();

                        ImGui.NextColumn();

                        if (ImGui.Checkbox($"安定", ref step.ExcludeCentered))
                            P.Config.Save();
                        if (ImGui.Checkbox($"結實", ref step.ExcludeSturdy))
                            P.Config.Save();
                        if (ImGui.Checkbox($"高效", ref step.ExcludePliant))
                            P.Config.Save();
                        if (ImGui.Checkbox($"大進展", ref step.ExcludeMalleable))
                            P.Config.Save();

                        ImGui.NextColumn();

                        if (ImGui.Checkbox($"長持續", ref step.ExcludePrimed))
                            P.Config.Save();
                        if (ImGui.Checkbox($"好兆頭", ref step.ExcludeGoodOmen))
                            P.Config.Save();

                        ImGui.Columns(1);
                        ImGui.PopStyleVar();

                        if (step.HasExcludeCondition)
                        {
                            ImGuiEx.CenterColumnText($"略過選項", true);
                            if (ImGui.Checkbox($"不略過，改用下列技能：", ref step.ReplaceOnExclude))
                                P.Config.Save();

                            if (step.ReplaceOnExclude)
                            {
                                if (ImGui.BeginCombo("###Select Replacement", step.ReplacementAction.NameOfAction()))
                                {
                                    if (ImGui.Selectable($"Artisan 建議"))
                                    {
                                        step.ReplacementAction = Skills.None;
                                        P.Config.Save();
                                    }

                                    ImGuiComponents.HelpMarker("依配方使用適合的預設求解器建議，例如一般配方使用標準配方求解器，專家配方使用專家配方求解器。");

                                    if (ImGui.Selectable($"加工連段"))
                                    {
                                        step.ReplacementAction = Skills.TouchCombo;
                                        P.Config.Save();
                                    }

                                    ImGuiComponents.HelpMarker("依實際使用的上一個技能，選用三段加工連段中的適當技能。搭配品質技能升級或依狀態略過時很實用。");

                                    if (ImGui.Selectable($"加工連段（精煉加工路線）"))
                                    {
                                        step.ReplacementAction = Skills.TouchComboRefined;
                                        P.Config.Save();
                                    }

                                    ImGuiComponents.HelpMarker($"與另一個加工連段相似，會依上一個技能在加工與精煉加工之間切換。");

                                    ImGui.Separator();

                                    foreach (var opt in Enum.GetValues(typeof(Skills)).Cast<Skills>().OrderBy(y => y.NameOfAction()))
                                    {
                                        if (ImGui.Selectable(opt.NameOfAction()))
                                        {
                                            step.ReplacementAction = opt;
                                            P.Config.Save();
                                        }
                                    }

                                    ImGui.EndCombo();
                                }
                            }
                        }
                        ImGui.EndChild();

                        if (ImGui.Button("刪除技能（按住 Ctrl）") && ImGui.GetIO().KeyCtrl)
                        {
                            SelectedMacro.Steps.RemoveAt(selectedStepIndex);
                            P.Config.Save();
                            if (selectedStepIndex == SelectedMacro.Steps.Count)
                                selectedStepIndex--;
                        }

                        if (ImGui.BeginCombo("###ReplaceAction", "替換技能"))
                        {
                            if (ImGui.Selectable($"Artisan 建議"))
                            {
                                step.Action = Skills.None;
                                P.Config.Save();
                            }

                            ImGuiComponents.HelpMarker("依配方使用適合的預設求解器建議，例如一般配方使用標準配方求解器，專家配方使用專家配方求解器。");

                            if (ImGui.Selectable($"加工連段"))
                            {
                                step.Action = Skills.TouchCombo;
                                P.Config.Save();
                            }

                            ImGuiComponents.HelpMarker("依實際使用的上一個技能，選用三段加工連段中的適當技能。搭配品質技能升級或依狀態略過時很實用。");

                            if (ImGui.Selectable($"加工連段（精煉加工路線）"))
                            {
                                step.Action = Skills.TouchComboRefined;
                                P.Config.Save();
                            }

                            ImGuiComponents.HelpMarker($"與另一個加工連段相似，會依上一個技能在加工與精煉加工之間切換。");

                            ImGui.Separator();

                            foreach (var opt in Enum.GetValues(typeof(Skills)).Cast<Skills>().OrderBy(y => y.NameOfAction()))
                            {
                                if (ImGui.Selectable(opt.NameOfAction()))
                                {
                                    step.Action = opt;
                                    P.Config.Save();
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.Text("調整技能順序");
                        if (selectedStepIndex > 0)
                        {
                            ImGui.SameLine();
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
                            {
                                SelectedMacro.Steps.Reverse(selectedStepIndex - 1, 2);
                                selectedStepIndex--;
                                P.Config.Save();
                            }
                        }

                        if (selectedStepIndex < SelectedMacro.Steps.Count - 1)
                        {
                            ImGui.SameLine();
                            if (selectedStepIndex == 0)
                            {
                                ImGui.Dummy(new Vector2(22));
                                ImGui.SameLine();
                            }

                            if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
                            {
                                SelectedMacro.Steps.Reverse(selectedStepIndex, 2);
                                selectedStepIndex++;
                                P.Config.Save();
                            }
                        }

                    }
                    ImGui.Columns(1);
                }
                else
                {
                    ImGui.Text($"巨集技能（每行一個技能）");
                    ImGuiComponents.HelpMarker("你可以像一般遊戲巨集一樣直接複製／貼上，也可以每行只輸入一個技能。\n例如：\n/ac Muscle Memory\n\n等同於：\n\nMuscle Memory\n\n也可以輸入 *（星號）或保留字串「Artisan Recommendation」，將 Artisan 的建議插入為其中一個步驟。");
                    ImGui.InputTextMultiline("###MacroEditor", ref _rawMacro, 10000000, new Vector2(ImGui.GetContentRegionAvail().X - 30f, ImGui.GetContentRegionAvail().Y - 30f));
                    if (ImGui.Button("儲存"))
                    {
                        var steps = MacroUI.ParseMacro(_rawMacro);
                        if (steps.Count > 0 && !SelectedMacro.Steps.SequenceEqual(steps))
                        {
                            selectedStepIndex = steps.Count - 1;
                            SelectedMacro.Steps = steps;
                            P.Config.Save();
                            DuoLog.Information($"巨集已更新");
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("儲存並關閉"))
                    {
                        var steps = MacroUI.ParseMacro(_rawMacro);
                        if (steps.Count > 0 && !SelectedMacro.Steps.SequenceEqual(steps))
                        {
                            selectedStepIndex = steps.Count - 1;
                            SelectedMacro.Steps = steps;
                            P.Config.Save();
                            DuoLog.Information($"巨集已更新");
                        }

                        Raweditor = !Raweditor;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("關閉"))
                    {
                        Raweditor = !Raweditor;
                    }
                }


                ImGuiEx.LineCentered("MTimeHead", delegate
                {
                    ImGuiEx.TextUnderlined($"預估巨集時間");
                });
                ImGuiEx.LineCentered("MTimeArtisan", delegate
                {
                    ImGuiEx.Text($"Artisan：{MacroUI.GetMacroLength(SelectedMacro)} 秒");
                });
                ImGuiEx.LineCentered("MTimeTeamcraft", delegate
                {
                    ImGuiEx.Text($"一般巨集：{MacroUI.GetTeamcraftMacroLength(SelectedMacro)} 秒");
                });
            }
            else
            {
                selectedStepIndex = -1;
            }
        }

        private void OnCraftStarted(Lumina.Excel.Sheets.Recipe recipe, CraftState craft, StepState initialStep, bool trial) => IsOpen = false;
    }
}
