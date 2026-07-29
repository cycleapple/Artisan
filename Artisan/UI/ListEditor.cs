namespace Artisan.UI;

using Autocraft;
using CraftingLists;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using global::Artisan.CraftingLogic;
using global::Artisan.CraftingLogic.Solvers;
using global::Artisan.GameInterop;
using global::Artisan.RawInformation.Character;
using global::Artisan.UI.Tables;
using Dalamud.Bindings.ImGui;
using IPC;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using OtterGui;
using OtterGui.Filesystem;
using OtterGui.Raii;
using PunishLib.ImGuiMethods;
using RawInformation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal class ListEditor : Window, IDisposable
{
    public bool Minimized = false;

    private Task? RegenerateTask = null;
    private CancellationTokenSource source = new CancellationTokenSource();
    private CancellationToken token;

    public bool Processing = false;

    internal List<uint> jobs = new();

    internal List<int> listMaterials = new();

    internal Dictionary<int, int> listMaterialsNew = new();

    internal string newListName = string.Empty;

    internal List<int> rawIngredientsList = new();

    internal Recipe? SelectedRecipe;

    internal Dictionary<uint, int> SelectedRecipeRawIngredients = new();

    internal Dictionary<uint, int> subtableList = new();

    private ListFolders ListsUI = new();

    private bool TidyAfter;

    private int timesToAdd = 1;

    public readonly RecipeSelector RecipeSelector;

    public readonly NewCraftingList SelectedList;

    private string newName = string.Empty;

    private bool RenameMode;

    internal string Search = string.Empty;

    public Dictionary<uint, int> SelectedListMateralsNew = new();

    public IngredientTable? Table;

    private bool ColourValidation = false;

    private bool HQSubcraftsOnly = false;

    private bool NeedsToRefreshTable = false;

    NewCraftingList? copyList;

    IngredientHelpers IngredientHelper = new();

    private bool hqSim = false;

    public ListEditor(int listId)
        : base($"製作清單編輯器###{listId}")
    {
        SelectedList = P.Config.NewCraftingLists.First(x => x.ID == listId);
        RecipeSelector = new RecipeSelector(SelectedList.ID);
        RecipeSelector.ItemAdded += RefreshTable;
        RecipeSelector.ItemDeleted += RefreshTable;
        RecipeSelector.ItemSkipTriggered += RefreshTable;
        IsOpen = true;
        P.ws.AddWindow(this);
        Size = new Vector2(1000, 600);
        SizeCondition = ImGuiCond.Appearing;
        ShowCloseButton = true;
        RespectCloseHotkey = false;
        NeedsToRefreshTable = true;

        if (P.Config.DefaultHQCrafts) HQSubcraftsOnly = true;
        if (P.Config.DefaultColourValidation) ColourValidation = true;
    }

    public async Task GenerateTableAsync(CancellationTokenSource source)
    {
        Table?.Dispose();
        var list = await IngredientHelper.GenerateList(SelectedList, source);
        if (list is null)
        {
            Svc.Log.Debug($"Table list empty, aborting.");
            return;
        }

        Table = new IngredientTable(list);
    }

    public void RefreshTable(object? sender, bool e)
    {
        token = source.Token;
        Table = null;
        P.UniversalsisClient.PlayerWorld = Svc.ClientState.LocalPlayer?.CurrentWorld.RowId;
        if (RegenerateTask == null || RegenerateTask.IsCompleted)
        {
            Svc.Log.Debug($"Starting regeneration");
            RegenerateTask = Task.Run(() => GenerateTableAsync(source), token);
        }
        else
        {
            Svc.Log.Debug($"Stopping and restarting regeneration");
            if (source != null)
                source.Cancel();

            source = new();
            token = source.Token;
            RegenerateTask = Task.Run(() => GenerateTableAsync(source), token);
        }
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

    private static bool GatherBuddy =>
        DalamudReflector.TryGetDalamudPlugin("GatherBuddy", out var gb, false, true);

    private static bool ItemVendor =>
        DalamudReflector.TryGetDalamudPlugin("Item Vendor Location", out var ivl, false, true);

    private static bool MonsterLookup =>
        DalamudReflector.TryGetDalamudPlugin("Monster Loot Hunter", out var mlh, false, true);

    private static unsafe void SearchItem(uint item) => ItemFinderModule.Instance()->SearchForItem(item);

    public class ListOrderCheck
    {
        public uint RecID;
        public int RecipeDepth = 0;
        public int RecipeDiff => Calculations.RecipeDifficulty(LuminaSheets.RecipeSheet[RecID]);
        public uint CraftType => LuminaSheets.RecipeSheet[RecID].CraftType.RowId;

        public int ListQuantity = 0;
        public ListItemOptions ops;
    }

    public async override void Draw()
    {
        var btn = ImGuiHelpers.GetButtonSize("開始執行製作清單");

        if (Endurance.Enable || CraftingListUI.Processing)
            ImGui.BeginDisabled();

        if (ImGui.Button("開始執行製作清單"))
        {
            CraftingListUI.selectedList = this.SelectedList;
            CraftingListUI.StartList();
            this.IsOpen = false;
        }

        if (Endurance.Enable || CraftingListUI.Processing)
            ImGui.EndDisabled();

        ImGui.SameLine();
        var export = ImGuiHelpers.GetButtonSize("匯出清單");

        if (ImGui.Button("匯出清單"))
        {
            ImGui.SetClipboardText(JsonConvert.SerializeObject(P.Config.NewCraftingLists.Where(x => x.ID == SelectedList.ID).First()));
            Notify.Success("已將清單匯出至剪貼簿。");
        }

        var restock = ImGuiHelpers.GetButtonSize("從雇員補充素材");
        if (RetainerInfo.ATools)
        {
            ImGui.SameLine();

            if (Endurance.Enable || CraftingListUI.Processing)
                ImGui.BeginDisabled();

            if (ImGui.Button($"從雇員補充素材"))
            {
                Task.Run(() => RetainerInfo.RestockFromRetainers(SelectedList));
            }

            if (Endurance.Enable || CraftingListUI.Processing)
                ImGui.EndDisabled();
        }
        else
        {
            ImGui.SameLine();

            if (!RetainerInfo.AToolsInstalled)
                ImGuiEx.Text(ImGuiColors.DalamudYellow, $"請安裝 Allagan Tools 以使用雇員功能。");

            if (RetainerInfo.AToolsInstalled && !RetainerInfo.AToolsEnabled)
                ImGuiEx.Text(ImGuiColors.DalamudYellow, $"請啟用 Allagan Tools 以使用雇員功能。");

            if (RetainerInfo.AToolsEnabled)
                ImGuiEx.Text(ImGuiColors.DalamudYellow, $"你已停用 Allagan Tools 整合。");
        }

        if (ImGui.BeginTabBar("CraftingListEditor", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("配方###Recipes"))
            {
                DrawRecipes();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("素材###Ingredients"))
            {
                if (NeedsToRefreshTable)
                {
                    RefreshTable(null, true);
                    NeedsToRefreshTable = false;
                }

                DrawIngredients();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("清單設定###List Settings"))
            {
                DrawListSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("從其他清單複製###Copy From Other List"))
            {
                DrawCopyFromList();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }


    private void DrawCopyFromList()
    {
        if (P.Config.NewCraftingLists.Count > 1)
        {
            ImGuiEx.TextWrapped($"選擇清單");
            ImGuiEx.SetNextItemFullWidth();
            if (ImGui.BeginCombo("###ListCopyCombo", copyList is null ? "" : copyList.Name))
            {
                if (ImGui.Selectable($""))
                {
                    copyList = null;
                }
                foreach (var list in P.Config.NewCraftingLists.Where(x => x.ID != SelectedList.ID))
                {
                    if (ImGui.Selectable($"{list.Name}###CopyList{list.ID}"))
                    {
                        copyList = list.JSONClone();
                    }
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.Text($"請先新增其他可供複製的清單");
        }

        if (copyList != null)
        {
            ImGui.Text($"將複製下列項目：");
            ImGui.Indent();
            if (ImGui.BeginListBox("###ItemList", new(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 30f)))
            {
                foreach (var rec in copyList.Recipes.Distinct())
                {
                    ImGui.Text($"- {LuminaSheets.RecipeSheet[rec.ID].ItemResult.Value.Name.ToDalamudString()} x{rec.Quantity}");
                }

                ImGui.EndListBox();
            }
            ImGui.Unindent();
            if (ImGui.Button($"複製項目"))
            {
                foreach (var recipe in copyList.Recipes)
                {
                    if (SelectedList.Recipes.Any(x => x.ID == recipe.ID))
                    {
                        SelectedList.Recipes.First(x => x.ID == recipe.ID).Quantity += recipe.Quantity;
                    }
                    else
                        SelectedList.Recipes.Add(new ListItem() { Quantity = recipe.Quantity, ID = recipe.ID });
                }
                Notify.Success($"已將所有項目從「{copyList.Name}」複製到「{SelectedList.Name}」。");
                RecipeSelector.Items = SelectedList.Recipes.Distinct().ToList();
                RefreshTable(null, true);
                P.Config.Save();
            }
        }
    }

    public void DrawRecipeSubTable()
    {
        int colCount = RetainerInfo.ATools ? 4 : 3;
        if (ImGui.BeginTable("###SubTableRecipeData", colCount, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("素材###Ingredient", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("需求量###Required", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("物品欄###Inventory", ImGuiTableColumnFlags.WidthFixed);
            if (RetainerInfo.ATools)
                ImGui.TableSetupColumn("雇員###Retainers", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();

            if (subtableList.Count == 0)
                subtableList = SelectedRecipeRawIngredients;

            try
            {
                foreach (var item in subtableList)
                {
                    if (LuminaSheets.ItemSheet.ContainsKey(item.Key))
                    {
                        if (CraftingListHelpers.SelectedRecipesCraftable[item.Key]) continue;
                        ImGui.PushID($"###SubTableItem{item}");
                        var sheetItem = LuminaSheets.ItemSheet[item.Key];
                        var name = sheetItem.Name.ToString();
                        var count = item.Value;

                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text($"{name}");
                        ImGui.TableNextColumn();
                        ImGui.Text($"{count}");
                        ImGui.TableNextColumn();
                        var invcount = CraftingListUI.NumberOfIngredient(item.Key);
                        if (invcount >= count)
                        {
                            var color = ImGuiColors.HealerGreen;
                            color.W -= 0.3f;
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(color));
                        }

                        ImGui.Text($"{invcount}");

                        if (RetainerInfo.ATools && RetainerInfo.CacheBuilt)
                        {
                            ImGui.TableNextColumn();
                            int retainerCount = 0;
                            retainerCount = RetainerInfo.GetRetainerItemCount(sheetItem.RowId);

                            ImGuiEx.Text($"{retainerCount}");

                            if (invcount + retainerCount >= count)
                            {
                                var color = ImGuiColors.HealerGreen;
                                color.W -= 0.3f;
                                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(color));
                            }

                        }

                        ImGui.PopID();

                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "SubTableRender");
            }

            ImGui.EndTable();
        }
    }
    public void DrawRecipeData()
    {
        var showOnlyCraftable = P.Config.ShowOnlyCraftable;

        if (ImGui.Checkbox("###ShowCraftableCheckbox", ref showOnlyCraftable))
        {
            P.Config.ShowOnlyCraftable = showOnlyCraftable;
            P.Config.Save();

            if (showOnlyCraftable)
            {
                RetainerInfo.TM.Abort();
                RetainerInfo.TM.Enqueue(async () => await RetainerInfo.LoadCache());
            }
        }

        ImGui.SameLine();
        ImGui.TextWrapped("只顯示目前素材足夠的配方（切換以重新整理）");

        if (P.Config.ShowOnlyCraftable && RetainerInfo.ATools)
        {
            var showOnlyCraftableRetainers = P.Config.ShowOnlyCraftableRetainers;
            if (ImGui.Checkbox("###ShowCraftableRetainersCheckbox", ref showOnlyCraftableRetainers))
            {
                P.Config.ShowOnlyCraftableRetainers = showOnlyCraftableRetainers;
                P.Config.Save();

                CraftingListUI.CraftableItems.Clear();
                RetainerInfo.TM.Abort();
                RetainerInfo.TM.Enqueue(async () => await RetainerInfo.LoadCache());
            }

            ImGui.SameLine();
            ImGui.TextWrapped("包含雇員");
        }

        var preview = SelectedRecipe is null
                          ? string.Empty
                          : $"{SelectedRecipe.Value.ItemResult.Value.Name.ToDalamudString().ToString()} ({LuminaSheets.ClassJobSheet[SelectedRecipe.Value.CraftType.RowId + 8].Abbreviation.ToString()})";

        if (ImGui.BeginCombo("選擇配方###Select Recipe", preview))
        {
            DrawRecipeList();

            ImGui.EndCombo();
        }

        if (SelectedRecipe != null)
        {
            if (ImGui.CollapsingHeader("配方資訊###Recipe Information")) DrawRecipeOptions();
            if (SelectedRecipeRawIngredients.Count == 0)
                CraftingListHelpers.AddRecipeIngredientsToList(SelectedRecipe, ref SelectedRecipeRawIngredients);

            if (ImGui.CollapsingHeader("基礎素材###Raw Ingredients"))
            {
                ImGui.Text("所需基礎素材");
                DrawRecipeSubTable();
            }

            ImGui.Spacing();
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().Length() / 2f);
            ImGui.TextWrapped("加入的製作次數");
            ImGui.SameLine();
            ImGui.InputInt("###TimesToAdd", ref timesToAdd, 1, 5);
            ImGui.PushItemWidth(-1f);

            if (timesToAdd < 1)
                ImGui.BeginDisabled();

            if (ImGui.Button("加入清單", new Vector2(ImGui.GetContentRegionAvail().X / 2, 30)))
            {
                SelectedListMateralsNew.Clear();
                listMaterialsNew.Clear();

                if (SelectedList.Recipes.Any(x => x.ID == SelectedRecipe.Value.RowId))
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).Quantity += checked(timesToAdd);
                }
                else
                {
                    SelectedList.Recipes.Add(new ListItem() { ID = SelectedRecipe.Value.RowId, Quantity = checked(timesToAdd) });
                }

                if (TidyAfter)
                    CraftingListHelpers.TidyUpList(SelectedList);

                if (SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions is null)
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions = new ListItemOptions { NQOnly = SelectedList.AddAsQuickSynth };
                }
                else
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions.NQOnly = SelectedList.AddAsQuickSynth;
                }

                RecipeSelector.Items = SelectedList.Recipes.Distinct().ToList();

                NeedsToRefreshTable = true;

                P.Config.Save();
                if (P.Config.ResetTimesToAdd)
                    timesToAdd = 1;
            }

            ImGui.SameLine();
            if (ImGui.Button("加入清單（包含所有半成品）", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
            {
                SelectedListMateralsNew.Clear();
                listMaterialsNew.Clear();

                CraftingListUI.AddAllSubcrafts(SelectedRecipe.Value, SelectedList, 1, timesToAdd);

                Svc.Log.Debug($"Adding: {SelectedRecipe.Value.ItemResult.Value.Name.ToDalamudString().ToString()} {timesToAdd} times");
                if (SelectedList.Recipes.Any(x => x.ID == SelectedRecipe.Value.RowId))
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).Quantity += timesToAdd;
                }
                else
                {
                    SelectedList.Recipes.Add(new ListItem() { ID = SelectedRecipe.Value.RowId, Quantity = timesToAdd });
                }

                if (TidyAfter)
                    CraftingListHelpers.TidyUpList(SelectedList);

                if (SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions is null)
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions = new ListItemOptions { NQOnly = SelectedList.AddAsQuickSynth };
                }
                else
                {
                    SelectedList.Recipes.First(x => x.ID == SelectedRecipe.Value.RowId).ListItemOptions.NQOnly = SelectedList.AddAsQuickSynth;
                }

                RecipeSelector.Items = SelectedList.Recipes.Distinct().ToList();
                RefreshTable(null, true);
                P.Config.Save();
                if (P.Config.ResetTimesToAdd)
                    timesToAdd = 1;
            }

            if (timesToAdd < 1)
                ImGui.EndDisabled();

            ImGui.Checkbox("加入後移除所有不必要的半成品", ref TidyAfter);
        }

        ImGui.Separator();

        if (ImGui.Button($"排序配方"))
        {
            List<ListItem> newList = new();
            List<ListOrderCheck> order = new();
            foreach (var item in SelectedList.Recipes.Distinct())
            {
                var orderCheck = new ListOrderCheck();
                var r = LuminaSheets.RecipeSheet[item.ID];
                orderCheck.RecID = r.RowId;
                int maxDepth = 0;
                foreach (var ing in r.Ingredients().Where(x => x.Amount > 0).Select(x => x.Item.RowId))
                {
                    CheckIngredientRecipe(ing, orderCheck);
                    if (orderCheck.RecipeDepth > maxDepth)
                    {
                        maxDepth = orderCheck.RecipeDepth;
                    }
                    orderCheck.RecipeDepth = 0;
                }
                orderCheck.RecipeDepth = maxDepth;
                orderCheck.ListQuantity = item.Quantity;
                orderCheck.ops = item.ListItemOptions ?? new ListItemOptions();
                order.Add(orderCheck);
            }

            foreach (var ord in order.OrderBy(x => x.RecipeDepth).ThenBy(x => x.RecipeDiff).ThenBy(x => x.CraftType))
            {
                newList.Add(new ListItem() { ID = ord.RecID, Quantity = ord.ListQuantity, ListItemOptions = ord.ops });
            }

            SelectedList.Recipes = newList;
            RecipeSelector.Items = SelectedList.Recipes.Distinct().ToList();
            P.Config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGuiEx.Tooltip($"依配方深度排序清單，再依難度排序。配方深度是指清單中有多少層素材依賴其他配方。\n\n" +
                $"例如：{LuminaSheets.RecipeSheet[35508].ItemResult.Value.Name.ToDalamudString()} 需要 {LuminaSheets.ItemSheet[36186].Name}，後者又需要 {LuminaSheets.ItemSheet[36189].Name}；若這些項目皆在清單內，該配方的深度便為 3。\n" +
                $"不依賴其他配方的項目深度為 1，因此會排在清單頂端，例如 {LuminaSheets.RecipeSheet[5299].ItemResult.Value.Name.ToDalamudString()}。\n\n" +
                $"最後會依遊戲內的製作難度排序，盡可能將相近的製作集中排列。");
        }

        Task.Run(() =>
        {
            listTime = CraftingListUI.GetListTimer(SelectedList);
        });
        string duration = listTime == TimeSpan.Zero ? "未知" : string.Format("{0:D2} 天 {1:D2} 小時 {2:D2} 分 {3:D2} 秒", listTime.Days, listTime.Hours, listTime.Minutes, listTime.Seconds);
        ImGui.SameLine();
        ImGui.Text($"預估清單時間：{duration}");
    }

    TimeSpan listTime;

    private void CheckIngredientRecipe(uint ing, ListOrderCheck orderCheck)
    {
        foreach (var result in SelectedList.Recipes.Distinct().Select(x => LuminaSheets.RecipeSheet[x.ID]))
        {
            if (result.ItemResult.RowId == ing)
            {
                orderCheck.RecipeDepth += 1;
                foreach (var subIng in result.Ingredients().Where(x => x.Amount > 0).Select(x => x.Item.RowId))
                {
                    CheckIngredientRecipe(subIng, orderCheck);
                }
                return;
            }
        }
    }

    private Dictionary<uint, string> RecipeLabels = new Dictionary<uint, string>();
    private void DrawRecipeList()
    {
        if (P.Config.ShowOnlyCraftable && !RetainerInfo.CacheBuilt)
        {
            if (RetainerInfo.ATools)
                ImGui.TextWrapped($"正在建立雇員快取：{(RetainerInfo.RetainerData.Values.Any() ? RetainerInfo.RetainerData.FirstOrDefault().Value.Count : "0")}/{LuminaSheets.RecipeSheet!.Select(x => x.Value).SelectMany(x => x.Ingredients()).Where(x => x.Item.RowId != 0 && x.Amount > 0).DistinctBy(x => x.Item.RowId).Count()}");
            ImGui.TextWrapped($"正在建立可製作項目清單：{CraftingListUI.CraftableItems.Count}/{LuminaSheets.RecipeSheet.Count}");
            ImGui.Spacing();
        }

        ImGui.Text("搜尋");
        ImGui.SameLine();
        ImGui.InputText("###RecipeSearch", ref Search, 150);
        if (ImGui.Selectable(string.Empty, SelectedRecipe == null))
        {
            SelectedRecipe = null;
        }

        if (P.Config.ShowOnlyCraftable && RetainerInfo.CacheBuilt)
        {
            foreach (var recipe in CraftingListUI.CraftableItems.Where(x => x.Value).Select(x => x.Key).Where(x => Regex.Match(x.ItemResult.Value.Name.GetText(true), Search, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Success))
            {
                if (recipe.Number == 0) continue;
                ImGui.PushID((int)recipe.RowId);
                if (!RecipeLabels.ContainsKey(recipe.RowId))
                {
                    RecipeLabels[recipe.RowId] = $"{recipe.ItemResult.Value.Name.ToDalamudString()} ({LuminaSheets.ClassJobSheet[recipe.CraftType.RowId + 8].Abbreviation} {recipe.RecipeLevelTable.Value.ClassJobLevel})";
                }
                var selected = ImGui.Selectable(RecipeLabels[recipe.RowId], recipe.RowId == SelectedRecipe?.RowId);

                if (selected)
                {
                    subtableList.Clear();
                    SelectedRecipeRawIngredients.Clear();
                    SelectedRecipe = recipe;
                }

                ImGui.PopID();
            }
        }
        else if (!P.Config.ShowOnlyCraftable)
        {
            foreach (var recipe in LuminaSheets.RecipeSheet.Values)
            {
                try
                {
                    if (recipe.ItemResult.RowId == 0) continue;
                    if (recipe.Number == 0) continue;
                    if (!string.IsNullOrEmpty(Search) && !Regex.Match(recipe.ItemResult.Value.Name.GetText(true), Search, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Success) continue;
                    if (!RecipeLabels.ContainsKey(recipe.RowId))
                    {
                        RecipeLabels[recipe.RowId] = $"{recipe.ItemResult.Value.Name.ToDalamudString()} ({LuminaSheets.ClassJobSheet[recipe.CraftType.RowId + 8].Abbreviation} {recipe.RecipeLevelTable.Value.ClassJobLevel})";
                    }
                    var selected = ImGui.Selectable(RecipeLabels[recipe.RowId], recipe.RowId == SelectedRecipe?.RowId);

                    if (selected)
                    {
                        subtableList.Clear();
                        SelectedRecipeRawIngredients.Clear();
                        SelectedRecipe = recipe;
                    }

                }
                catch (Exception ex)
                {
                    Svc.Log.Error(ex, "DrawRecipeList");
                }
            }
        }
    }


    private void DrawRecipeOptions()
    {
        {
            List<uint> craftingJobs = LuminaSheets.RecipeSheet.Values.Where(x => x.ItemResult.Value.Name.ToDalamudString().ToString() == SelectedRecipe.Value.ItemResult.Value.Name.ToDalamudString().ToString()).Select(x => x.CraftType.Value.RowId + 8).ToList();
            string[]? jobstrings = LuminaSheets.ClassJobSheet.Values.Where(x => craftingJobs.Any(y => y == x.RowId)).Select(x => x.Abbreviation.ToString()).ToArray();
            ImGui.Text($"製作職業：{string.Join(", ", jobstrings)}");
        }

        var ItemsRequired = SelectedRecipe.Value.Ingredients();

        int numRows = RetainerInfo.ATools ? 6 : 5;
        if (ImGui.BeginTable("###RecipeTable", numRows, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("素材###Ingredient", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("需求量###Required", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("物品欄###Inventory", ImGuiTableColumnFlags.WidthFixed);
            if (RetainerInfo.ATools)
                ImGui.TableSetupColumn("雇員###Retainers", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("取得方式###Method", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("來源###Source", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            try
            {
                foreach (var value in ItemsRequired.Where(x => x.Amount > 0))
                {
                    jobs.Clear();
                    string ingredient = LuminaSheets.ItemSheet[value.Item.RowId].Name.ToString();
                    Recipe? ingredientRecipe = CraftingListHelpers.GetIngredientRecipe(value.Item.RowId);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiEx.Text($"{ingredient}");
                    ImGui.TableNextColumn();
                    ImGuiEx.Text($"{value.Amount}");
                    ImGui.TableNextColumn();
                    var invCount = CraftingListUI.NumberOfIngredient(value.Item.RowId);
                    ImGuiEx.Text($"{invCount}");

                    if (invCount >= value.Amount)
                    {
                        var color = ImGuiColors.HealerGreen;
                        color.W -= 0.3f;
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(color));
                    }

                    ImGui.TableNextColumn();
                    if (RetainerInfo.ATools && RetainerInfo.CacheBuilt)
                    {
                        int retainerCount = 0;
                        retainerCount = RetainerInfo.GetRetainerItemCount(value.Item.RowId);

                        ImGuiEx.Text($"{retainerCount}");

                        if (invCount + retainerCount >= value.Amount)
                        {
                            var color = ImGuiColors.HealerGreen;
                            color.W -= 0.3f;
                            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.ColorConvertFloat4ToU32(color));
                        }

                        ImGui.TableNextColumn();
                    }

                    if (ingredientRecipe is not null)
                    {
                        if (ImGui.Button($"製作###search{ingredientRecipe.Value.RowId}"))
                        {
                            SelectedRecipe = ingredientRecipe;
                        }
                    }
                    else
                    {
                        ImGui.Text("採集");
                    }

                    ImGui.TableNextColumn();
                    if (ingredientRecipe is not null)
                    {
                        try
                        {
                            jobs.AddRange(LuminaSheets.RecipeSheet.Values.Where(x => x.ItemResult.RowId == ingredientRecipe.Value.ItemResult.RowId).Select(x => x.CraftType.RowId + 8));
                            string[]? jobstrings = LuminaSheets.ClassJobSheet.Values.Where(x => jobs.Any(y => y == x.RowId)).Select(x => x.Abbreviation.ToString()).ToArray();
                            ImGui.Text(string.Join(", ", jobstrings));
                        }
                        catch (Exception ex)
                        {
                            Svc.Log.Error(ex, "JobStrings");
                        }

                    }
                    else
                    {
                        try
                        {
                            var gatheringItem = LuminaSheets.GatheringItemSheet?.Where(x => x.Value.Item.RowId == value.Item.RowId).FirstOrDefault().Value;
                            if (gatheringItem != null)
                            {
                                var jobs = LuminaSheets.GatheringPointBaseSheet?.Values.Where(x => x.Item.Any(y => y.RowId == gatheringItem.Value.RowId)).Select(x => x.GatheringType).ToList();
                                List<string> tempArray = new();
                                if (jobs!.Any(x => x.Value.RowId is 0 or 1)) tempArray.Add(LuminaSheets.ClassJobSheet[16].Abbreviation.ToString());
                                if (jobs!.Any(x => x.Value.RowId is 2 or 3)) tempArray.Add(LuminaSheets.ClassJobSheet[17].Abbreviation.ToString());
                                if (jobs!.Any(x => x.Value.RowId is 4 or 5)) tempArray.Add(LuminaSheets.ClassJobSheet[18].Abbreviation.ToString());
                                ImGui.Text($"{string.Join(", ", tempArray)}");
                                continue;
                            }

                            var spearfish = LuminaSheets.SpearfishingItemSheet?.Where(x => x.Value.Item.Value.RowId == value.Item.RowId).FirstOrDefault().Value;
                            if (spearfish != null && spearfish.Value.Item.Value.Name.ToString() == ingredient)
                            {
                                ImGui.Text($"{LuminaSheets.ClassJobSheet[18].Abbreviation.ToString()}");
                                continue;
                            }

                            var fishSpot = LuminaSheets.FishParameterSheet?.Where(x => x.Value.Item.RowId == value.Item.RowId).FirstOrDefault().Value;
                            if (fishSpot != null)
                            {
                                ImGui.Text($"{LuminaSheets.ClassJobSheet[18].Abbreviation.ToString()}");
                            }


                        }
                        catch (Exception ex)
                        {
                            Svc.Log.Error(ex, "JobStrings");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "RecipeIngreds");
            }

            ImGui.EndTable();
        }

    }

    public override void OnClose()
    {
        Table?.Dispose();
        source.Cancel();
        P.ws.RemoveWindow(this);
    }

    private void DrawIngredients()
    {
        if (SelectedList.ID != 0)
        {
            if (SelectedList.Recipes.Count > 0)
            {
                DrawTotalIngredientsTable();
            }
            else
            {
                ImGui.Text($"請將項目加入清單，以顯示素材分頁。");
            }
        }
    }
    private void DrawTotalIngredientsTable()
    {
        if (Table == null && RegenerateTask.IsCompleted)
        {
            if (ImGui.Button($"建立表格時發生問題。要再試一次嗎？"))
            {
                RefreshTable(null, true);
            }
            return;
        }
        if (Table == null)
        {
            ImGui.TextUnformatted($"素材表格仍在建立中，請稍候。");
            var a = IngredientHelper.CurrentIngredient;
            var b = IngredientHelper.MaxIngredient;
            ImGui.ProgressBar((float)a / b, new(ImGui.GetContentRegionAvail().X, default), $"{a * 100.0f / b:f2}% ({a}/{b})");
            return;
        }
        ImGui.BeginChild("###IngredientsListTable", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (ColourValidation ? (RetainerInfo.ATools ? 90f.Scale() : 60f.Scale()) : 30f.Scale())));
        Table._nameColumn.ShowColour = ColourValidation;
        Table._inventoryColumn.HQOnlyCrafts = HQSubcraftsOnly;
        Table._retainerColumn.HQOnlyCrafts = HQSubcraftsOnly;
        Table._nameColumn.ShowHQOnly = HQSubcraftsOnly;
        Table.Draw(ImGui.GetTextLineHeightWithSpacing());
        ImGui.EndChild();

        ImGui.Checkbox($"只顯示 HQ 製作素材", ref HQSubcraftsOnly);

        ImGuiComponents.HelpMarker($"對於可製作的素材，只計入物品欄{(RetainerInfo.ATools ? "與雇員" : "")}中的 HQ 數量。");

        ImGui.SameLine();
        ImGui.Checkbox("啟用顏色檢查", ref ColourValidation);

        ImGui.SameLine();

        if (ImGui.GetIO().KeyShift)
        {
            if (ImGui.Button($"以純文字匯出全部所需素材"))
            {
                StringBuilder sb = new();
                foreach (var item in Table.ListItems.Where(x => x.Required > 0))
                {
                    sb.AppendLine($"{item.Required}x {item.Data.Name}");
                }

                if (!string.IsNullOrEmpty(sb.ToString()))
                {
                    ImGui.SetClipboardText(sb.ToString());
                    Notify.Success($"已將全部所需素材複製到剪貼簿。");
                }
                else
                {
                    Notify.Error($"沒有需要複製的素材。");
                }
            }
        }
        else
        {
            if (ImGui.Button($"以純文字匯出尚缺素材"))
            {
                StringBuilder sb = new();
                foreach (var item in Table.ListItems.Where(x => x.Remaining > 0))
                {
                    sb.AppendLine($"{item.Remaining}x {item.Data.Name}");
                }

                if (!string.IsNullOrEmpty(sb.ToString()))
                {
                    ImGui.SetClipboardText(sb.ToString());
                    Notify.Success($"已將尚缺素材複製到剪貼簿。");
                }
                else
                {
                    Notify.Error($"沒有尚缺素材可供複製。");
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGuiEx.Tooltip($"按住 Shift 可由尚缺素材切換為全部所需素材。");
            }

        }


        ImGui.SameLine();
        if (ImGui.Button("需要說明嗎？"))
            ImGui.OpenPopup("HelpPopup");

        if (ColourValidation)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.HealerGreen);
            ImGui.BeginDisabled(true);
            ImGui.Button("", new Vector2(23, 23));
            ImGui.EndDisabled();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 7);
            ImGui.Text($"－物品欄已有全部所需素材{(SelectedList.SkipIfEnough && SelectedList.SkipLiteral ? "" : "，或已持有使用此素材製成的成品，因此無需準備")}");

            if (RetainerInfo.ATools)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudOrange);
                ImGui.BeginDisabled(true);
                ImGui.Button("", new Vector2(23, 23));
                ImGui.EndDisabled();
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 7);
                ImGui.Text($"－雇員與物品欄合計已有全部所需素材");
            }

            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.ParsedBlue);
            ImGui.BeginDisabled(true);
            ImGui.Button("", new Vector2(23, 23));
            ImGui.EndDisabled();
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 7);
            ImGui.Text($"－物品欄與可製作數量合計可滿足全部需求。");
        }


        var windowSize = new Vector2(1024 * ImGuiHelpers.GlobalScale,
            ImGui.GetTextLineHeightWithSpacing() * 13 + 2 * ImGui.GetFrameHeightWithSpacing());
        ImGui.SetNextWindowSize(windowSize);
        ImGui.SetNextWindowPos((ImGui.GetIO().DisplaySize - windowSize) / 2);

        using var popup = ImRaii.Popup("HelpPopup",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.Modal);
        if (!popup)
            return;

        ImGui.TextWrapped($"素材表格會列出製作清單內所有項目所需的內容，包括物品欄中的素材數量、素材來源，以及可採集區域等資訊。");
        ImGui.Dummy(new Vector2(0));
        ImGui.BulletText($"點選欄位標題可透過輸入文字或預設條件篩選結果。");
        ImGui.BulletText($"在欄位標題上按右鍵，可顯示／隱藏欄位或調整欄寬。");
        ImGui.BulletText($"在素材名稱上按右鍵，可開啟包含更多選項的選單。");
        ImGui.BulletText($"拖曳欄位標題間亮起的空隙，可以重新排列欄位。");
        ImGui.BulletText($"沒有看到項目嗎？請檢查是否有紅色欄位標題，這表示該欄位正在套用篩選。在標題上按右鍵可清除篩選。");
        ImGui.BulletText($"安裝下列插件可擴充表格功能：\n- Allagan Tools（啟用所有雇員功能）\n- Item Vendor Lookup\n- Gatherbuddy\n- Monster Loot Hunter");
        ImGui.BulletText($"提示：採集時可篩選「尚缺數量」與「來源」，並依採集區域排序，以減少移動時間。");

        ImGui.SetCursorPosY(windowSize.Y - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y);
        if (ImGui.Button("關閉說明", -Vector2.UnitX))
            ImGui.CloseCurrentPopup();
    }

    private void DrawListSettings()
    {
        ImGui.BeginChild("ListSettings", ImGui.GetContentRegionAvail(), false);
        var skipIfEnough = SelectedList.SkipIfEnough;
        if (ImGui.Checkbox("略過不必要的素材製作", ref skipIfEnough))
        {
            SelectedList.SkipIfEnough = skipIfEnough;
            P.Config.Save();
        }
        ImGuiComponents.HelpMarker($"略過清單中不再需要製作的素材。");

        if (skipIfEnough)
        {
            ImGui.Indent();
            if (ImGui.Checkbox("依清單產量判斷是否略過", ref SelectedList.SkipLiteral))
            {
                P.Config.Save();
            }

            ImGuiComponents.HelpMarker("若物品欄中的素材數量少於清單從零開始製作時會產出的數量，便會繼續製作。\n\n" +
                "[配方產量] × [製作次數] 小於 [物品欄數量] 時略過。\n\n" +
                "製作清單外項目所需的素材時可使用此選項，例如部隊工房工程。\n\n" +
                "這也會調整素材表格的尚缺欄位與顏色檢查，不再計入使用該素材製作的其他成品。");
            ImGui.Unindent();
        }

        if (!RawInformation.Character.CharacterInfo.MateriaExtractionUnlocked())
            ImGui.BeginDisabled();

        var materia = SelectedList.Materia;
        if (ImGui.Checkbox("自動精製魔晶石", ref materia))
        {
            SelectedList.Materia = materia;
            P.Config.Save();
        }

        if (!RawInformation.Character.CharacterInfo.MateriaExtractionUnlocked())
        {
            ImGui.EndDisabled();

            ImGuiComponents.HelpMarker("此角色尚未解鎖精製魔晶石，將忽略此設定。");
        }
        else
            ImGuiComponents.HelpMarker("裝備的精煉度達到 100% 時，自動精製魔晶石。");

        var repair = SelectedList.Repair;
        if (ImGui.Checkbox("自動修理", ref repair))
        {
            SelectedList.Repair = repair;
            P.Config.Save();
        }

        ImGuiComponents.HelpMarker($"啟用後，任一裝備的耐久度降至設定門檻時，Artisan 會自動修理。\n\n目前裝備最低耐久度為 {RepairManager.GetMinEquippedPercent()}%，交由商店修理的費用為 {RepairManager.GetNPCRepairPrice()} 金幣。\n\n若無法使用暗物質自行修理，則會嘗試尋找附近的修理工。");

        if (SelectedList.Repair)
        {
            ImGui.PushItemWidth(200);
            if (ImGui.SliderInt("##repairp", ref SelectedList.RepairPercent, 10, 100, "%d%%"))
                P.Config.Save();
        }

        if (ImGui.Checkbox("新加入清單的項目預設使用簡易製作", ref SelectedList.AddAsQuickSynth))
            P.Config.Save();

        ImGui.EndChild();
    }

    private void DrawRecipes()
    {
        DrawRecipeData();

        ImGui.Spacing();
        RecipeSelector.Draw(RecipeSelector.maxSize + 16f + ImGui.GetStyle().ScrollbarSize);
        ImGui.SameLine();

        if (RecipeSelector.Current?.ID > 0)
            ItemDetailsWindow.Draw("配方選項", DrawRecipeSettingsHeader, DrawRecipeSettings);
    }

    private void DrawRecipeSettings()
    {
        var selectedListItem = RecipeSelector.Items[RecipeSelector.CurrentIdx].ID;
        var recipe = LuminaSheets.RecipeSheet[RecipeSelector.Current.ID];
        var count = RecipeSelector.Items[RecipeSelector.CurrentIdx].Quantity;

        ImGui.TextWrapped("調整數量");
        ImGuiEx.SetNextItemFullWidth(-30);
        if (ImGui.InputInt("###AdjustQuantity", ref count))
        {
            if (count >= 0)
            {
                SelectedList.Recipes.First(x => x.ID == selectedListItem).Quantity = count;
                P.Config.Save();
            }

            NeedsToRefreshTable = true;
        }

        if (SelectedList.Recipes.First(x => x.ID == selectedListItem).ListItemOptions is null)
        {
            SelectedList.Recipes.First(x => x.ID == selectedListItem).ListItemOptions = new();
        }

        var options = SelectedList.Recipes.First(x => x.ID == selectedListItem).ListItemOptions;

        if (recipe.CanQuickSynth)
        {
            var NQOnly = options.NQOnly;
            if (ImGui.Checkbox("此項目使用簡易製作", ref NQOnly))
            {
                options.NQOnly = NQOnly;
                P.Config.Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("套用至全部###QuickSynthAll"))
            {
                foreach (var r in SelectedList.Recipes.Where(n => LuminaSheets.RecipeSheet[n.ID].CanQuickSynth))
                {
                    if (r.ListItemOptions == null)
                    { r.ListItemOptions = new(); }
                    r.ListItemOptions.NQOnly = options.NQOnly;
                }
                Notify.Success($"已將簡易製作設定套用至所有清單項目。");
                P.Config.Save();
            }

            if (NQOnly && !P.Config.UseConsumablesQuickSynth)
            {
                if (ImGui.Checkbox("尚未啟用簡易製作消耗品。要立即啟用嗎？", ref P.Config.UseConsumablesQuickSynth))
                    P.Config.Save();
            }
        }
        else
        {
            ImGui.TextWrapped("此項目無法使用簡易製作。");
        }

        // Retrieve the list of recipes matching the selected recipe name from the preprocessed lookup table.
        var matchingRecipes = LuminaSheets.recipeLookup[selectedListItem.NameOfRecipe()].ToList();

        if (matchingRecipes.Count > 1)
        {
            var pre = $"{LuminaSheets.ClassJobSheet[recipe.CraftType.RowId + 8].Abbreviation.ToString()}";
            ImGui.TextWrapped("切換製作職業");
            ImGuiEx.SetNextItemFullWidth(-30);
            if (ImGui.BeginCombo("###SwitchJobCombo", pre))
            {
                foreach (var altJob in matchingRecipes)
                {
                    var altJ = $"{LuminaSheets.ClassJobSheet[altJob.CraftType.RowId + 8].Abbreviation.ToString()}";
                    if (ImGui.Selectable($"{altJ}"))
                    {
                        try
                        {
                            if (SelectedList.Recipes.Any(x => x.ID == altJob.RowId))
                            {
                                SelectedList.Recipes.First(x => x.ID == altJob.RowId).Quantity += SelectedList.Recipes.First(x => x.ID == selectedListItem).Quantity;
                                SelectedList.Recipes.Remove(SelectedList.Recipes.First(x => x.ID == selectedListItem));
                                RecipeSelector.Items.RemoveAt(RecipeSelector.CurrentIdx);
                                RecipeSelector.Current = RecipeSelector.Items.First(x => x.ID == altJob.RowId);
                                RecipeSelector.CurrentIdx = RecipeSelector.Items.IndexOf(RecipeSelector.Current);
                            }
                            else
                            {
                                SelectedList.Recipes.First(x => x.ID == selectedListItem).ID = altJob.RowId;
                                RecipeSelector.Items[RecipeSelector.CurrentIdx].ID = altJob.RowId;
                                RecipeSelector.Current = RecipeSelector.Items[RecipeSelector.CurrentIdx];
                            }
                            NeedsToRefreshTable = true;

                            P.Config.Save();
                        }
                        catch
                        {

                        }
                    }
                }

                ImGui.EndCombo();
            }
        }

        var config = P.Config.RecipeConfigs.GetValueOrDefault(selectedListItem) ?? new();
        {
            if (config.DrawFood(true))
            {
                P.Config.RecipeConfigs[selectedListItem] = config;
                P.Config.Save();
            }

            ImGui.SameLine();

            if (ImGui.Button($"套用至全部###FoodApplyAll"))
            {
                foreach (var r in SelectedList.Recipes.Distinct())
                {
                    var o = P.Config.RecipeConfigs.GetValueOrDefault(r.ID) ?? new();
                    o.requiredFood = config.requiredFood;
                    o.requiredFoodHQ = config.requiredFoodHQ;
                    P.Config.RecipeConfigs[r.ID] = o;
                }
                P.Config.Save();
            }
        }
        {
            if (config.DrawPotion(true))
            {
                P.Config.RecipeConfigs[selectedListItem] = config;
                P.Config.Save();
            }

            ImGui.SameLine();

            if (ImGui.Button($"套用至全部###PotionApplyAll"))
            {
                foreach (var r in SelectedList.Recipes.Distinct())
                {
                    var o = P.Config.RecipeConfigs.GetValueOrDefault(r.ID) ?? new();
                    o.requiredPotion = config.requiredPotion;
                    o.requiredPotionHQ = config.requiredPotionHQ;
                    P.Config.RecipeConfigs[r.ID] = o;
                }
                P.Config.Save();
            }
        }

        {
            if (config.DrawManual(true))
            {
                P.Config.RecipeConfigs[selectedListItem] = config;
                P.Config.Save();
            }

            ImGui.SameLine();

            if (ImGui.Button($"套用至全部###ManualApplyAll"))
            {
                foreach (var r in SelectedList.Recipes.Distinct())
                {
                    var o = P.Config.RecipeConfigs.GetValueOrDefault(r.ID) ?? new();
                    o.requiredManual = config.requiredManual;
                    P.Config.RecipeConfigs[r.ID] = o;
                }
                P.Config.Save();
            }
        }

        {
            if (config.DrawSquadronManual(true))
            {
                P.Config.RecipeConfigs[selectedListItem] = config;
                P.Config.Save();
            }

            ImGui.SameLine();

            if (ImGui.Button($"套用至全部###SquadManualApplyAll"))
            {
                foreach (var r in SelectedList.Recipes.Distinct())
                {
                    var o = P.Config.RecipeConfigs.GetValueOrDefault(r.ID) ?? new();
                    o.requiredSquadronManual = config.requiredSquadronManual;
                    P.Config.RecipeConfigs[r.ID] = o;
                }
                P.Config.Save();
            }
        }

        var stats = CharacterStats.GetBaseStatsForClassHeuristic((Job)((uint)Job.CRP + recipe.CraftType.RowId));
        stats.AddConsumables(new(config.RequiredFood, config.RequiredFoodHQ), new(config.RequiredPotion, config.RequiredPotionHQ), CharacterInfo.FCCraftsmanshipbuff);
        var craft = Crafting.BuildCraftStateForRecipe(stats, (Job)((uint)Job.CRP + recipe.CraftType.RowId), recipe);
        if (config.DrawSolver(craft))
        {
            P.Config.RecipeConfigs[selectedListItem] = config;
            P.Config.Save();
        }
        
        ImGuiEx.TextV("製作需求：");
        ImGui.SameLine();
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
        ImGui.SameLine(137.6f.Scale());
        ImGui.TextWrapped($"難度：{craft.CraftProgress}｜耐久：{craft.CraftDurability}｜品質：{(craft.CraftCollectible ? craft.CraftQualityMin3 : craft.CraftQualityMax)}");
        ImGuiComponents.HelpMarker($"顯示製作需求：完成製作所需的作業進展、配方耐久，以及達到最高品質等級所需的品質目標（收藏品）。可依此資訊選擇合適的巨集。");

        ImGui.Checkbox($"假設初始品質為最大值（模擬器）", ref hqSim);

        var solverHint = Simulator.SimulatorResult(recipe, config, craft, out var hintColor, hqSim);
        if (!recipe.IsExpert)
            ImGuiEx.TextWrapped(hintColor, solverHint);
        else
            ImGuiEx.TextWrapped($"請在模擬器中執行此配方以取得結果。");
    }

    private void DrawRecipeSettingsHeader()
    {
        if (!RenameMode)
        {
            if (IconButtons.IconTextButton(FontAwesomeIcon.Pen, $"{SelectedList.Name.Replace($"%", "%%")}"))
            {
                newName = SelectedList.Name;
                RenameMode = true;
            }
        }
        else
        {
            if (ImGui.InputText("###RenameMode", ref newName, 200, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (newName.Length > 0)
                {
                    SelectedList.Name = newName;
                    P.Config.Save();
                }
                RenameMode = false;
            }
        }
    }

    public void Dispose()
    {
        source.Cancel();
        RecipeSelector.ItemAdded -= RefreshTable;
        RecipeSelector.ItemDeleted -= RefreshTable;
        RecipeSelector.ItemSkipTriggered -= RefreshTable;
    }
}

internal class RecipeSelector : ItemSelector<ListItem>
{
    public float maxSize = 100;

    private readonly NewCraftingList List;

    public RecipeSelector(int list)
        : base(
            P.Config.NewCraftingLists.First(x => x.ID == list).Recipes.Distinct().ToList(),
            Flags.Add | Flags.Delete | Flags.Move)
    {
        List = P.Config.NewCraftingLists.First(x => x.ID == list);
    }

    protected override bool Filtered(int idx)
    {
        return false;
    }

    protected override bool OnAdd(string name)
    {
        if (name.Trim().All(char.IsDigit))
        {
            var id = Convert.ToUInt32(name);
            if (LuminaSheets.RecipeSheet.Values.Any(x => x.ItemResult.RowId == id))
            {
                var recipe = LuminaSheets.RecipeSheet.Values.First(x => x.ItemResult.RowId == id);
                if (recipe.Number == 0) return false;
                if (List.Recipes.Any(x => x.ID == recipe.RowId))
                {
                    List.Recipes.First(x => x.ID == recipe.RowId).Quantity += 1;
                }
                else
                {
                    List.Recipes.Add(new ListItem() { ID = recipe.RowId, Quantity = 1 });
                }

                if (!Items.Any(x => x.ID == recipe.RowId)) Items.Add(List.Recipes.First(x => x.ID == recipe.RowId));
            }
        }
        else
        {
            if (LuminaSheets.RecipeSheet.Values.TryGetFirst(
                    x => x.ItemResult.Value.Name.ToDalamudString().ToString().Equals(name, StringComparison.CurrentCultureIgnoreCase),
                    out var recipe))
            {
                if (recipe.Number == 0) return false;
                if (List.Recipes.Any(x => x.ID == recipe.RowId))
                {
                    List.Recipes.First(x => x.ID == recipe.RowId).Quantity += 1;
                }
                else
                {
                    List.Recipes.Add(new ListItem() { ID = recipe.RowId, Quantity = 1 });
                }

                if (!Items.Any(x => x.ID == recipe.RowId)) Items.Add(List.Recipes.First(x => x.ID == recipe.RowId));
            }
        }

        P.Config.Save();

        return true;
    }

    protected override bool OnDelete(int idx)
    {
        var ItemId = Items[idx];
        List.Recipes.Remove(ItemId);
        Items.RemoveAt(idx);
        P.Config.Save();
        return true;
    }

    protected override bool OnDraw(int idx, out bool changes)
    {
        changes = false;
        var ItemId = Items[idx];
        var itemCount = ItemId.Quantity;
        var yield = LuminaSheets.RecipeSheet[ItemId.ID].AmountResult * itemCount;
        var label =
            $"{idx + 1}. {ItemId.ID.NameOfRecipe()} x{itemCount}{(yield != itemCount ? $" ({yield} total)" : string.Empty)}";
        maxSize = ImGui.CalcTextSize(label).X > maxSize ? ImGui.CalcTextSize(label).X : maxSize;

        if (ItemId.ListItemOptions is null)
        {
            ItemId.ListItemOptions = new();
            P.Config.Save();
        }

        using (var col = ImRaii.PushColor(ImGuiCol.Text, itemCount == 0 || ItemId.ListItemOptions.Skipping ? ImGuiColors.DalamudRed : ImGuiColors.DalamudWhite))
        {
            var res = ImGui.Selectable(label, idx == CurrentIdx);
            ImGuiEx.Tooltip($"按右鍵可{(ItemId.ListItemOptions.Skipping ? "啟用" : "略過")}此配方。");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                ItemId.ListItemOptions.Skipping = !ItemId.ListItemOptions.Skipping;
                changes = true;
                P.Config.Save();
            }
            return res;
        }
    }

    protected override bool OnMove(int idx1, int idx2)
    {
        List.Recipes.Move(idx1, idx2);
        Items.Move(idx1, idx2);
        P.Config.Save();
        return true;
    }
}

internal class ListFolders : ItemSelector<NewCraftingList>
{
    public ListFolders()
        : base(P.Config.NewCraftingLists, Flags.Add | Flags.Delete | Flags.Move | Flags.Filter | Flags.Duplicate)
    {
        CurrentIdx = -1;
    }

    protected override string DeleteButtonTooltip()
    {
        return "永久刪除此製作清單。\r\n按住 Ctrl 並點選。\r\n此操作無法復原。";
    }

    protected override bool Filtered(int idx)
    {
        return Filter.Length != 0 && !Items[idx].Name.Contains(
                   Filter,
                   StringComparison.InvariantCultureIgnoreCase);
    }

    protected override bool OnAdd(string name)
    {
        var list = new NewCraftingList { Name = name };
        list.SetID();
        list.Save(true);

        return true;
    }

    protected override bool OnDelete(int idx)
    {
        if (P.ws.Windows.TryGetFirst(
                x => x.WindowName.Contains(CraftingListUI.selectedList.ID.ToString()) && x.GetType() == typeof(ListEditor),
                out var window))
        {
            P.ws.RemoveWindow(window);
        }

        P.Config.NewCraftingLists.RemoveAt(idx);
        P.Config.Save();

        if (!CraftingListUI.Processing)
            CraftingListUI.selectedList = new NewCraftingList();
        return true;
    }

    protected override bool OnDraw(int idx, out bool changes)
    {
        changes = false;
        if (CraftingListUI.Processing && CraftingListUI.selectedList.ID == P.Config.NewCraftingLists[idx].ID)
            ImGui.BeginDisabled();

        using var id = ImRaii.PushId(idx);
        var selected = ImGui.Selectable($"{P.Config.NewCraftingLists[idx].Name} (ID: {P.Config.NewCraftingLists[idx].ID})", idx == CurrentIdx);
        if (selected)
        {
            if (!P.ws.Windows.Any(x => x.WindowName.Contains(P.Config.NewCraftingLists[idx].ID.ToString())))
            {
                Interface.SetupValues();
                ListEditor editor = new(P.Config.NewCraftingLists[idx].ID);
            }
            else
            {
                P.ws.Windows.TryGetFirst(
                    x => x.WindowName.Contains(P.Config.NewCraftingLists[idx].ID.ToString()),
                    out var window);
                window.BringToFront();
            }

            if (!CraftingListUI.Processing)
                CraftingListUI.selectedList = P.Config.NewCraftingLists[idx];
        }

        if (!CraftingListUI.Processing)
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                if (CurrentIdx == idx)
                {
                    CurrentIdx = -1;
                    CraftingListUI.selectedList = new NewCraftingList();
                }
                else
                {
                    CurrentIdx = idx;
                    CraftingListUI.selectedList = P.Config.NewCraftingLists[idx];
                }
            }
        }

        if (CraftingListUI.Processing && CraftingListUI.selectedList.ID == P.Config.NewCraftingLists[idx].ID)
            ImGui.EndDisabled();

        return selected;
    }

    protected override bool OnDuplicate(string name, int idx)
    {
        var baseList = P.Config.NewCraftingLists[idx];
        NewCraftingList newList = new NewCraftingList();
        newList = baseList.JSONClone();
        newList.Name = name;
        newList.SetID();
        newList.Save();
        return true;
    }

    protected override bool OnMove(int idx1, int idx2)
    {
        P.Config.NewCraftingLists.Move(idx1, idx2);
        return true;
    }
}
