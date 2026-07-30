using Artisan.RawInformation;
using Artisan.UI;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using PunishLib.ImGuiMethods;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Artisan.CraftingLists
{
    internal static class Teamcraft
    {
        internal static string importListName = "";
        internal static string importListPreCraft = "";
        internal static string importListItems = "";
        internal static bool openImportWindow = false;
        private static bool precraftQS = false;
        private static bool finalitemQS = false;

        internal static void DrawTeamCraftListButtons()
        {
            string labelText = "Teamcraft 清單";
            var labelLength = ImGui.CalcTextSize(labelText);
            ImGui.SetCursorPosX((ImGui.GetContentRegionMax().X - labelLength.X) * 0.5f);
            ImGui.TextColored(ImGuiColors.ParsedGreen, labelText);
                if (IconButtons.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Download, "匯入", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
            {
                openImportWindow = true;
            }
            OpenTeamcraftImportWindow();
            if (CraftingListUI.selectedList.ID != 0)
            {
                if (IconButtons.IconTextButton(Dalamud.Interface.FontAwesomeIcon.Upload, "匯出", new Vector2(ImGui.GetContentRegionAvail().X, 30), true))
                {
                    ExportSelectedListToTC();
                }
            }
        }

        private static void ExportSelectedListToTC()
        {
            string baseUrl = "https://ffxivteamcraft.com/import/";
            string exportItems = "";

            var sublist = CraftingListUI.selectedList.Recipes.Distinct().Reverse().ToList();
            for (int i = 0; i < sublist.Count; i++)
            {
                if (i >= sublist.Count) break;

                int number = CraftingListUI.selectedList.Recipes[i].Quantity;
                var recipe = LuminaSheets.RecipeSheet[sublist[i].ID];
                var ItemId = recipe.ItemResult.Value.RowId;

                Svc.Log.Debug($"{recipe.ItemResult.Value.Name.ToDalamudString().ToString()} {sublist.Count}");
                ExtractRecipes(sublist, recipe);
            }

            foreach (var item in sublist)
            {
                int number = item.Quantity;
                var recipe = LuminaSheets.RecipeSheet[item.ID];
                var ItemId = recipe.ItemResult.Value.RowId;

                exportItems += $"{ItemId},null,{number};";
            }

            exportItems = exportItems.TrimEnd(';');

            var plainTextBytes = Encoding.UTF8.GetBytes(exportItems);
            string base64 = Convert.ToBase64String(plainTextBytes);

            Svc.Log.Debug($"{baseUrl}{base64}");
            ImGui.SetClipboardText($"{baseUrl}{base64}");
            Notify.Success("連結已複製到剪貼簿");
        }

        private static void ExtractRecipes(List<ListItem> sublist, Recipe recipe)
        {
            foreach (var ing in recipe.Ingredients().Where(x => x.Amount > 0))
            {
                var subRec = CraftingListHelpers.GetIngredientRecipe(ing.Item.RowId);
                if (subRec != null)
                {
                    if (sublist.Any(x => x.ID == subRec.Value.RowId))
                    {
                        foreach (var subIng in subRec.Value.Ingredients().Where(x => x.Amount > 0))
                        {
                            var subSubRec = CraftingListHelpers.GetIngredientRecipe(subIng.Item.RowId);
                            if (subSubRec != null)
                            {
                                if (sublist.Any(x => x.ID == subSubRec.Value.RowId))
                                {
                                    sublist.RemoveAll(x => x.ID == subSubRec.Value.RowId);
                                }
                            }
                        }

                        sublist.RemoveAll(x => x.ID == subRec.Value.RowId);
                    }
                }
            }
        }

        private static void OpenTeamcraftImportWindow()
        {
            if (!openImportWindow) return;


            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.1f, 0.2f, 1f));
            ImGui.SetNextWindowSize(new Vector2(1, 1), ImGuiCond.Appearing);
            if (ImGui.Begin("Teamcraft 匯入###TCImport", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("清單名稱");
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("清單匯入說明。\r\n\r\n" +
                    "步驟 1：在 Teamcraft 開啟包含欲製作物品的清單。\r\n\r\n" +
                    "步驟 2：找到半成品區段並點擊「Copy as Text」。\r\n\r\n" +
                    "步驟 3：貼到此視窗的「半成品」欄位。\r\n\r\n" +
                    "步驟 4：對最終成品區段重複步驟 2 與 3。\r\n\r\n" +
                    "步驟 5：輸入清單名稱並點擊「匯入」。");
                ImGui.InputText("###ImportListName", ref importListName, 50);
                ImGui.Text("半成品");
                ImGui.InputTextMultiline("###PrecraftItems", ref importListPreCraft, 5000000, new Vector2(ImGui.GetContentRegionAvail().X, 100));

                if (!P.Config.DefaultListQuickSynth)
                    ImGui.Checkbox("以快速製作匯入###ImportQSPre", ref precraftQS);
                else
                    ImGui.TextWrapped("由於已啟用預設設定，這些物品會嘗試以快速製作方式加入。");
                ImGui.Text("最終成品");
                ImGui.InputTextMultiline("###FinalItems", ref importListItems, 5000000, new Vector2(ImGui.GetContentRegionAvail().X, 100));
                if (!P.Config.DefaultListQuickSynth)
                    ImGui.Checkbox("以快速製作匯入###ImportQSFinal", ref finalitemQS);
                else
                    ImGui.TextWrapped("由於已啟用預設設定，這些物品會嘗試以快速製作方式加入。");

                try
                {
                    if (ImGui.Button("匯入"))
                    {
                        NewCraftingList? importedList = ParseImport(precraftQS, finalitemQS);
                        if (importedList is not null)
                        {
                            if (GenericHelpers.IsNullOrEmpty(importedList.Name))
                                importedList.Name = importedList.Recipes.FirstOrDefault().ID.NameOfRecipe();
                            importedList.SetID();
                            importedList.Save();
                            openImportWindow = false;
                            importListName = "";
                            importListPreCraft = "";
                            importListItems = "";

                        }
                        else
                        {
                            Notify.Error("匯入的清單沒有任何物品，請檢查內容後再試一次。");
                        }

                    }
                }
                catch (Exception ex)
                {
                    ex.Log();
                }
                ImGui.SameLine();
                if (ImGui.Button("取消"))
                {
                    openImportWindow = false;
                    importListName = "";
                    importListPreCraft = "";
                    importListItems = "";
                }
                ImGui.End();
            }
            ImGui.PopStyleColor();
        }

        private static NewCraftingList? ParseImport(bool precraftQS, bool finalitemQS)
        {
            if (string.IsNullOrEmpty(importListName) && string.IsNullOrEmpty(importListItems) && string.IsNullOrEmpty(importListPreCraft)) return null;
            NewCraftingList output = new NewCraftingList();
            output.Name = importListName;
            using (System.IO.StringReader reader = new System.IO.StringReader(importListPreCraft))
            {
                string line = "";
                while ((line = reader.ReadLine()!) != null)
                {
                    var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    if (parts[0][^1] == 'x')
                    {
                        int numberOfItem = int.Parse(parts[0].Substring(0, parts[0].Length - 1));
                        var builder = new StringBuilder();
                        for (int i = 1; i < parts.Length; i++)
                        {
                            builder.Append(parts[i]);
                            builder.Append(" ");
                        }
                        var item = builder.ToString().Trim();
                        Svc.Log.Debug($"{numberOfItem} x {item}");

                        var recipe = GenericHelpers.FindRow<Recipe>(x => x.ItemResult.ValueNullable?.RowId > 0 && x.ItemResult.ValueNullable?.Name.ToDalamudString().ToString() == item);
                        if (recipe?.RowId > 0)
                        {
                            int quantity = (int)Math.Ceiling(numberOfItem / (double)recipe.Value.AmountResult);
                            if (output.Recipes.Any(x => x.ID == recipe.Value.RowId))
                                output.Recipes.First(x => x.ID == recipe.Value.RowId).Quantity += quantity;
                            else
                                output.Recipes.Add(new ListItem() { ID = recipe.Value.RowId, Quantity = quantity, ListItemOptions = new() });

                            if (precraftQS && recipe.Value.CanQuickSynth)
                                output.Recipes.First(x => x.ID == recipe.Value.RowId).ListItemOptions.NQOnly = true;
                        }
                    }

                }
            }
            using (System.IO.StringReader reader = new System.IO.StringReader(importListItems))
            {
                string line = "";
                while ((line = reader.ReadLine()!) != null)
                {
                    var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    if (parts[0][^1] == 'x')
                    {
                        int numberOfItem = int.Parse(parts[0].Substring(0, parts[0].Length - 1));
                        var builder = new StringBuilder();
                        for (int i = 1; i < parts.Length; i++)
                        {
                            builder.Append(parts[i]);
                            builder.Append(" ");
                        }
                        var item = builder.ToString().Trim();
                        if (DebugTab.Debug) Svc.Log.Debug($"{numberOfItem} x {item}");

                        var recipe = GenericHelpers.FindRow<Recipe>(x => x.ItemResult.ValueNullable?.RowId > 0 && x.ItemResult.ValueNullable?.Name.ToDalamudString().ToString() == item);
                        if (recipe?.RowId > 0)
                        {
                            int quantity = (int)Math.Ceiling(numberOfItem / (double)recipe.Value.AmountResult);
                            if (output.Recipes.Any(x => x.ID == recipe.Value.RowId))
                                output.Recipes.First(x => x.ID == recipe.Value.RowId).Quantity += quantity;
                            else
                                output.Recipes.Add(new ListItem() { ID = recipe.Value.RowId, Quantity = quantity, ListItemOptions = new() });

                            if (finalitemQS && recipe.Value.CanQuickSynth)
                                output.Recipes.First(x => x.ID == recipe.Value.RowId).ListItemOptions.NQOnly = true;
                        }
                    }

                }
            }

            if (output.Recipes.Count == 0) return null;

            return output;
        }
    }
}
