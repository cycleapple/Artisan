using Artisan.Autocraft;
using Artisan.CraftingLists;
using Artisan.GameInterop;
using Artisan.IPC;
using Artisan.RawInformation;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using System.Collections.Generic;
using System.Linq;

namespace Artisan.UI
{
    internal class CraftMenuWindowUI : Window
    {
        public bool EnableMacroOptions { get; set; }
        
        public CraftMenuWindowUI(string windowName, ImGuiWindowFlags flags) : base(windowName, flags)
        {
            IsOpen = false;
            ShowCloseButton = false;
            RespectCloseHotkey = false;
            DisableWindowSounds = true;
            PositionCondition = ImGuiCond.Appearing;
            
            TitleBarButtons.Add(new()
            {
                Icon = FontAwesomeIcon.Cog,
                ShowTooltip = () => ImGui.SetTooltip("開啟設定"),
                Click = (x) => P.PluginUi.IsOpen = true,
            });
        }

        public override bool DrawConditions()
        {
            return IsOpen;
        }

        public override void PreDraw()
        {
            if (P.Config.DisableTheme)
            {
                return;
            }
            
            P.Style.Push();
            P.StylePushed = true;
        }

        public override void PostDraw()
        {
            if (!P.StylePushed)
            {
                return;
            }
            
            P.Style.Pop();
            P.StylePushed = false;
        }

        public override void Draw()
        {
            if (!IsOpen)
            {
                return;
            }
            
            var autoMode = P.Config.AutoMode;

            if (ImGui.Checkbox("自動執行技能模式", ref autoMode))
            {
                P.Config.AutoMode = autoMode;
                P.Config.Save();
            }
            
            var enable = Endurance.Enable;

            if (!CraftingListFunctions.HasItemsForRecipe(Endurance.RecipeID) && !Endurance.Enable)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Checkbox("耐久製作模式", ref enable))
            {
                Endurance.ToggleEndurance(enable);
            }

            if (!CraftingListFunctions.HasItemsForRecipe(Endurance.RecipeID) && !Endurance.Enable)
            {
                ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    var recipe = LuminaSheets.RecipeSheet!.First(x => x.Key == Endurance.RecipeID).Value;
                    ImGui.BeginTooltip();
                    ImGui.Text($"無法開始耐久製作，因為缺少此配方所需的素材。\r\n缺少：{string.Join(", ", PreCrafting.MissingIngredients(recipe))}");
                    ImGui.EndTooltip();
                }
			}

			if (Crafting.MaterialMiracleCharges() > 0)
			{
				bool useMatMiracle = LuminaSheets.RecipeSheet[Endurance.RecipeID].IsExpert ? P.Config.ExpertSolverConfig.UseMaterialMiracle : P.Config.UseMaterialMiracle;
				int delayMatMiracle = LuminaSheets.RecipeSheet[Endurance.RecipeID].IsExpert ? P.Config.ExpertSolverConfig.MinimumStepsBeforeMiracle : P.Config.MinimumStepsBeforeMiracle;
				bool multiMatMiracle = P.Config.MaterialMiracleMulti;
				if (ImGui.Checkbox("使用比爾格的奇蹟", ref useMatMiracle))
				{
					if (LuminaSheets.RecipeSheet[Endurance.RecipeID].IsExpert)
						P.Config.ExpertSolverConfig.UseMaterialMiracle = useMatMiracle;
					else
						P.Config.UseMaterialMiracle = useMatMiracle;
				}
				if (ImGui.SliderInt("嘗試使用比爾格的奇蹟前至少執行的步數", ref delayMatMiracle, 0, 20))
				{
					if (LuminaSheets.RecipeSheet[Endurance.RecipeID].IsExpert)
						P.Config.ExpertSolverConfig.MinimumStepsBeforeMiracle = delayMatMiracle;
					else
						P.Config.MinimumStepsBeforeMiracle = delayMatMiracle;
				}

				if (false == LuminaSheets.RecipeSheet[Endurance.RecipeID].IsExpert)
				{
					if (ImGui.Checkbox("允許多次使用比爾格的奇蹟", ref multiMatMiracle))
						P.Config.MaterialMiracleMulti = multiMatMiracle;
				}
			}

            if (EnableMacroOptions)
            {
                ImGui.Spacing();

                if (SimpleTweaks.IsFocusTweakEnabled())
                {
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, $@"警告：你已啟用 Simple Tweaks 的「自動聚焦配方搜尋」功能。此功能與 Artisan 高度不相容，建議停用。");
                }

                if (Endurance.RecipeID == 0)
                {
                    return;
                }
                
                var config = P.Config.RecipeConfigs.GetValueOrDefault(Endurance.RecipeID) ?? new();
                
                if (!config.Draw(Endurance.RecipeID))
                {
                    return;
                }
                
                P.Config.RecipeConfigs[Endurance.RecipeID] = config;
                P.Config.Save();
            }
        }
    }
}
