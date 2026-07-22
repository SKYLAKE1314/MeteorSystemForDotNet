Imports System.Windows
Imports Newtonsoft.Json

''' <summary>
''' 模板參數編輯對話框：載入後可修改閾值、解碼期望、OCR期望文字
''' </summary>
Public Class TemplateParamEditDialog

    Private ReadOnly _groupPath As String
    Private _targetCamDirs As List(Of String)

    Public Sub New(groupPath As String)
        InitializeComponent()
        _groupPath = groupPath
        TxtTemplateName.Text = IO.Path.GetFileName(groupPath)
        LoadCurrentParams()
    End Sub

    ''' <summary>
    ''' 從第一個 cam 目錄的 config.json 載入現有參數
    ''' </summary>
    Private Sub LoadCurrentParams()
        Try
            _targetCamDirs = IO.Directory.GetDirectories(_groupPath, "cam*").
                Where(Function(d) IO.File.Exists(IO.Path.Combine(d, "template.png"))).
                ToList()

            If _targetCamDirs.Count = 0 Then
                ' 舊格式：直接在 group 根目錄
                _targetCamDirs = New List(Of String) From {_groupPath}
            End If

            ' 從第一個 cam 目錄讀取 config
            Dim configPath = IO.Path.Combine(_targetCamDirs(0), "config.json")
            If Not IO.File.Exists(configPath) Then Return

            Dim json = IO.File.ReadAllText(configPath)
            Dim config = JsonConvert.DeserializeObject(Of TemplateConfig)(json)
            If config Is Nothing Then Return

            TxtThreshold.Text = config.Threshold.ToString("F2")
            ChkEnableBarcode.IsChecked = config.EnableBarcode
            TxtBarcodeExpected.Text = If(config.BarcodeExpectedText, "")
            ChkEnableOcr.IsChecked = config.EnableOcr
            TxtOcrExpected.Text = If(config.OcrExpectedText, "")

        Catch ex As Exception
            Logger.Warn($"[TemplateParamEdit] 載入參數失敗: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnSave_Click(sender As Object, e As RoutedEventArgs)
        Try
            ' 驗證閾值
            Dim threshold As Double
            If Not Double.TryParse(TxtThreshold.Text, threshold) OrElse
               threshold < 0.01 OrElse threshold > 1.0 Then
                MeteorMessageBox.Show("閾值必須在 0.01 ~ 1.00 之間", "輸入錯誤",
                                MessageBoxButton.OK, MessageBoxImage.Warning)
                Return
            End If

            ' 對所有 cam 目錄儲存 config
            For Each camDir In _targetCamDirs
                SaveConfigToDir(camDir, threshold)
            Next

            ' 若此模板是目前選用的，同步更新 TemplateSnapshot
            SyncSnapshotIfActive()

            Logger.Info($"[TemplateParamEdit] 已儲存模板參數: {IO.Path.GetFileName(_groupPath)}")
            DialogResult = True
            Close()

        Catch ex As Exception
            MeteorMessageBox.Show($"儲存失敗: {ex.Message}", "錯誤",
                            MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub SaveConfigToDir(camDir As String, threshold As Double)
        Dim configPath = IO.Path.Combine(camDir, "config.json")

        ' 若已有 config，讀取後覆寫；否則建立新的
        Dim config As TemplateConfig
        If IO.File.Exists(configPath) Then
            Dim existing = IO.File.ReadAllText(configPath)
            config = JsonConvert.DeserializeObject(Of TemplateConfig)(existing)
        End If

        If config Is Nothing Then config = New TemplateConfig()

        config.Threshold = threshold
        config.EnableBarcode = ChkEnableBarcode.IsChecked.GetValueOrDefault(False)
        config.BarcodeExpectedText = TxtBarcodeExpected.Text.Trim()
        config.EnableOcr = ChkEnableOcr.IsChecked.GetValueOrDefault(False)
        config.OcrExpectedText = TxtOcrExpected.Text.Trim()

        IO.File.WriteAllText(configPath,
            JsonConvert.SerializeObject(config, Formatting.Indented))
    End Sub

    ''' <summary>
    ''' 若正在使用的 Snapshot 屬於此模板，同步更新其參數（立即生效）
    ''' </summary>
    Private Sub SyncSnapshotIfActive()
        Try
            Dim snapshot = TemplateSnapshotStore.Load()
            If snapshot Is Nothing OrElse
               String.IsNullOrWhiteSpace(snapshot.TemplatePath) Then Return

            ' 判斷 snapshot 是否屬於此模板群組
            Dim snapGroup = IO.Path.GetDirectoryName(
                IO.Path.GetDirectoryName(snapshot.TemplatePath))
            If Not String.Equals(snapGroup, _groupPath,
                StringComparison.OrdinalIgnoreCase) Then Return

            Dim threshold As Double
            Double.TryParse(TxtThreshold.Text, threshold)

            snapshot.Threshold = threshold
            snapshot.EnableBarcode = ChkEnableBarcode.IsChecked.GetValueOrDefault(False)
            snapshot.BarcodeExpectedText = TxtBarcodeExpected.Text.Trim()
            snapshot.EnableOcr = ChkEnableOcr.IsChecked.GetValueOrDefault(False)
            snapshot.OcrExpectedText = TxtOcrExpected.Text.Trim()

            TemplateSnapshotStore.Save(snapshot)
            Logger.Info("[TemplateParamEdit] 已同步更新 TemplateSnapshot")
        Catch ex As Exception
            Logger.Warn($"[TemplateParamEdit] Snapshot 同步失敗: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        DialogResult = False
        Close()
    End Sub

End Class
