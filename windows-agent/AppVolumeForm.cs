namespace STMediaBridge;

internal sealed class AppVolumeForm : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly ImageList icons = new() { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
    private readonly List<Icon> ownedIcons = new();
    public AppVolumeForm(AppCatalog catalog)
    {
        Text = ProductInfo.DisplayName + " — " + TrayContext.T("앱별 볼륨 제어", "App Volume Controls");
        ClientSize = new Size(740, 480); MinimumSize = new Size(600, 350);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); Padding = new Padding(16);
        var help = new Label { Dock = DockStyle.Top, Height = 58,
            Text = TrayContext.T("체크하면 SmartThings에 PC <앱 이름>이 생성됩니다.\n체크를 해제하면 해당 기기가 삭제됩니다. 앱을 종료해도 선택은 유지됩니다.",
                "Check an app to create PC <App name> in SmartThings.\nUncheck to delete its device. Selections remain when apps close.") };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true,
            FullRowSelect = true, HideSelection = false, SmallImageList = icons };
        list.Columns.Add(TrayContext.T("앱", "App"), 320);
        list.Columns.Add(TrayContext.T("오디오 세션", "Audio session"), 145);
        list.Columns.Add(TrayContext.T("음량", "Volume"), 75);
        list.Columns.Add(TrayContext.T("음소거", "Muted"), 90);
        var hint = new Label { Dock = DockStyle.Bottom, Height = 32,
            Text = TrayContext.T("앱이 없으면 소리를 한 번 재생하세요. 허브 연결 후 선택이 반영됩니다.",
                "Play audio to discover an app. Selections sync when the hub is connected.") };
        Controls.Add(list); Controls.Add(help); Controls.Add(hint);
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
                        item.SubItems.AddRange(new[] { "", "", "" });
                        try
                        {
                            var appIcon = Icon.ExtractAssociatedIcon(row.App.ExecutablePath);
                            if (appIcon is not null) { ownedIcons.Add(appIcon); icons.Images.Add(row.App.Key, appIcon); item.ImageKey = row.App.Key; }
                        }
                        catch (Exception) { }
                        list.Items.Add(item);
                    }
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
            try { catalog.SetExposed(list.Items[e.Index].Name, e.NewValue == CheckState.Checked); }
            catch (Exception ex)
            {
                e.NewValue = e.CurrentValue;
                MessageBox.Show(ex.Message, ProductInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        timer.Tick += (_, _) => RefreshApps();
        RefreshApps(); timer.Start();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
        if (disposing) { icons.Dispose(); foreach (var icon in ownedIcons) icon.Dispose(); }
    }
}
