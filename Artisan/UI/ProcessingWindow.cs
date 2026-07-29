using Artisan.CraftingLists;
using Artisan.GameInterop;
using Artisan.RawInformation;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System;

namespace Artisan.UI
{
    internal class ProcessingWindow : Window
    {
        public ProcessingWindow() : base("正在執行製作清單###ProcessingList", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
        {
            IsOpen = true;
            ShowCloseButton = false;
            RespectCloseHotkey = false;
            SizeCondition = ImGuiCond.Appearing;
        }

        public override bool DrawConditions()
        {
            if (CraftingListUI.Processing)
                return true;

            return false;
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

        public unsafe override void Draw()
        {
            if (CraftingListUI.Processing)
            {
                CraftingListFunctions.ProcessList(CraftingListUI.selectedList);

                //if (ImGuiEx.AddHeaderIcon("OpenConfig", FontAwesomeIcon.Cog, new ImGuiEx.HeaderIconOptions() { Tooltip = "Open Config" }))
                //{
                //    P.PluginUi.IsOpen = true;
                //}

                ImGui.Text($"正在執行：{CraftingListUI.selectedList.Name}");
                ImGui.Separator();
                ImGui.Spacing();
                if (CraftingListUI.CurrentProcessedItem != 0)
                {
                    ImGuiEx.TextV($"製作中：{LuminaSheets.RecipeSheet[CraftingListUI.CurrentProcessedItem].ItemResult.Value.Name.ToDalamudString().ToString()}");
                    ImGuiEx.TextV($"目前項目進度：{CraftingListUI.CurrentProcessedItemCount} / {CraftingListUI.CurrentProcessedItemListCount}");
                    ImGuiEx.TextV($"清單總進度：{CraftingListFunctions.CurrentIndex + 1} / {CraftingListUI.selectedList.ExpandedList.Count}");

                    string duration = CraftingListFunctions.ListEndTime == TimeSpan.Zero ? "未知" : string.Format("{0:D2} 天 {1:D2} 小時 {2:D2} 分 {3:D2} 秒", CraftingListFunctions.ListEndTime.Days, CraftingListFunctions.ListEndTime.Hours, CraftingListFunctions.ListEndTime.Minutes, CraftingListFunctions.ListEndTime.Seconds);
                    ImGuiEx.TextV($"預估剩餘時間：{duration}");

                }

                if (!CraftingListFunctions.Paused)
                {
                    if (ImGui.Button("暫停"))
                    {
                        CraftingListFunctions.Paused = true;
                        P.TM.Abort();
                        CraftingListFunctions.CLTM.Abort();
                        PreCrafting.Tasks.Clear();
                    }
                }
                else
                {
                    if (ImGui.Button("繼續"))
                    {
                        if (Crafting.CurState is Crafting.State.IdleNormal or Crafting.State.IdleBetween)
                        {
                            var recipe = LuminaSheets.RecipeSheet[CraftingListUI.CurrentProcessedItem];
                            PreCrafting.Tasks.Add((() => PreCrafting.TaskSelectRecipe(recipe), default));
                        }

                        CraftingListFunctions.Paused = false;
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("取消"))
                {
                    CraftingListUI.Processing = false;
                    CraftingListFunctions.Paused = false;
                    P.TM.Abort();
                    CraftingListFunctions.CLTM.Abort();
                    PreCrafting.Tasks.Clear();
                    Crafting.CraftFinished -= CraftingListUI.UpdateListTimer;
                }
            }
        }
    }
}
