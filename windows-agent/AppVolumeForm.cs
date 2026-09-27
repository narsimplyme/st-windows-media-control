namespace STMediaBridge;

internal sealed class AppVolumeForm : Form
{
    private readonly ImageList icons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<Icon> ownedIcons = new();
    public AppVolumeForm(AppCatalog catalog)
    {
        Text = ProductInfo.DisplayName + " — " + TrayContext.T("앱별 볼륨 제어", "App Volume Controls");
        ClientSize = new Size(740, 480); MinimumSize = new Size(600, 350);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); Padding = new Padding(16);
        var help = new Label { Dock = DockStyle.Top, Height = 86,
            Text = TrayContext.T("최대 5개 앱을 SmartThings 기기의 App 1~5에 연결합니다.\n아래 슬롯 번호를 보고 SmartThings의 연필 버튼으로 앱 이름을 직접 지정하세요.\n체크 해제 시 해당 슬롯만 비워집니다. 앱을 종료해도 선택은 유지됩니다.",
                "Select up to 5 apps for App 1–5 inside your SmartThings device.\nMatch the Slot column to App 1–5 and use the SmartThings pencil to name each app.\nUnchecking frees only that slot. Selections remain when apps close.") };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
            FullRowSelect = true, HideSelection = false, SmallImageList = icons };
        list.Columns.Add(TrayContext.T("앱", "App"), 235);
        list.Columns.Add(TrayContext.T("오디오 세션", "Audio session"), 145);
        list.Columns.Add(TrayContext.T("음량", "Volume"), 75);
        list.Columns.Add(TrayContext.T("음소거", "Muted"), 90);
        list.Columns.Add(TrayContext.T("슬롯", "Slot"), 80);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 32,
            Text = TrayContext.T("앱에서 소리를 재생한 뒤 ‘앱 새로고침’을 누르세요.",
                "Play audio in an app, then click Refresh apps.") };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        var refresh = new Button { Text = TrayContext.T("앱 새로고침", "Refresh apps"), AutoSize = true };
        toolbar.Controls.Add(refresh);
        Controls.Add(list); Controls.Add(toolbar); Controls.Add(help); Controls.Add(hint);
        var updating = false;
        void RefreshApps()
        {
            updating = true; list.BeginUpdate();
            try
            {
                foreach (var row in catalog.Read())
                {
                    var item = list.Items[row.App.Key];
                    if (item is null)
                    {
                        item = new ListViewItem(row.App.Name) { Name = row.App.Key };
                        item.SubItems.AddRange(new[] { "", "", "", "" });
                        try
                        {
                            var appIcon = Icon.ExtractAssociatedIcon(row.App.ExecutablePath);
                            if (appIcon is not null) { ownedIcons.Add(appIcon); icons.Images.Add(row.App.Key, appIcon); item.ImageKey = row.App.Key; }
                        }
                        catch (Exception) { }
                        list.Items.Add(item);
                    }
                    item.SubItems[4].Text = row.App.Slot == 0 ? "—" : "App " + row.App.Slot;
                    item.Text = row.App.Name; item.Checked = row.App.Exposed;
                    item.SubItems[1].Text = row.State.Active ? TrayContext.T("있음", "Available") : TrayContext.T("없음", "Not running");
                    item.SubItems[2].Text = row.State.Active ? row.State.Volume + "%" : "—";
                    item.SubItems[3].Text = row.State.Active ? (row.State.Muted ? TrayContext.T("예", "Yes") : TrayContext.T("아니요", "No")) : "—";
                }
            }
            finally { list.EndUpdate(); updating = false; }
        }
        list.ItemCheck += (_, e) =>
        {
            if (updating) return;
            try
            {
                var item = list.Items[e.Index];
                catalog.SetExposed(item.Name, e.NewValue == CheckState.Checked);
                var slot = catalog.Read().Single(x => x.App.Key == item.Name).App.Slot;
                item.SubItems[4].Text = slot == 0 ? "—" : "App " + slot;
            }
            catch (Exception ex)
            {
                e.NewValue = e.CurrentValue;
                MessageBox.Show(ex.Message, ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        refresh.Click += (_, _) => RefreshApps();
        RefreshApps();
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { icons.Dispose(); foreach (var icon in ownedIcons) icon.Dispose(); }
    }
}
