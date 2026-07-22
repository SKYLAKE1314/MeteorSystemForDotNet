Imports System.Collections.ObjectModel
Imports System.Windows

Public Class TemplateManageDialog

    Private ReadOnly _groupPath As String
    Private ReadOnly _templates As New ObservableCollection(Of TemplateItemVM)()
    Public Property SelectedSampleFileName As String
    Public Property SelectedSampleMeta As TemplateTrainingStore.TrainingSampleMeta

    Public Class TemplateItemVM
        Public Property FileName As String
        Public Property FilePath As String
        Public Property CreatedAtDisplay As String
        Public Property LastMatchedAtDisplay As String
        Public Property RoiDisplay As String
        Public Property SampleCountDisplay As String
    End Class

    Public Sub New(groupPath As String)
        InitializeComponent()
        _groupPath = TemplateTrainingStore.NormalizeGroupPath(groupPath)
        TemplateGrid.ItemsSource = _templates
        LoadTemplates()
    End Sub

    Private Sub LoadTemplates()
        _templates.Clear()

        Try
            If String.IsNullOrWhiteSpace(_groupPath) OrElse Not IO.Directory.Exists(_groupPath) Then
                TxtEmptyHint.Text = "找不到訓練資料夾"
                Return
            End If

            Dim samples = TemplateTrainingStore.GetTrainingSamples(_groupPath)
            Dim sampleDir = IO.Path.Combine(_groupPath, "training", "samples")

            If samples.Count = 0 Then
                TxtEmptyHint.Text = "目前沒有訓練樣本"
                Return
            End If

            For Each s In samples
                Dim filePath = IO.Path.Combine(sampleDir, s.FileName)
                _templates.Add(New TemplateItemVM With {
                    .FileName = s.FileName,
                    .FilePath = filePath,
                    .CreatedAtDisplay = DateTimeOffset.FromUnixTimeMilliseconds(s.CreatedAt).ToString("yyyy-MM-dd HH:mm"),
                    .LastMatchedAtDisplay = DateTimeOffset.FromUnixTimeMilliseconds(s.LastMatchedAt).ToString("yyyy-MM-dd HH:mm"),
                    .RoiDisplay = $"{s.RoiX},{s.RoiY},{s.RoiW},{s.RoiH}",
                    .SampleCountDisplay = samples.Count.ToString()
                })
            Next

            TxtEmptyHint.Text = $"共 {samples.Count} 筆"
            If TemplateGrid.SelectedItem Is Nothing AndAlso _templates.Count > 0 Then
                TemplateGrid.SelectedIndex = 0
            End If
        Catch ex As Exception
            MeteorMessageBox.Show($"載入模板失敗：{ex.Message}")
        End Try
    End Sub

    Private Sub BtnDelete_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = CType(sender, Button)
        Dim fileName = CStr(btn.Tag)
        If String.IsNullOrWhiteSpace(fileName) Then Return

        Dim result = MeteorMessageBox.Show($"確定要刪除 {fileName} 嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question)
        If result <> MessageBoxResult.Yes Then Return

        Try
            If TemplateTrainingStore.DeleteTrainingSample(_groupPath, fileName) Then
                LoadTemplates()
                MeteorMessageBox.Show("已刪除", "成功", MessageBoxButton.OK, MessageBoxImage.Information)
            Else
                MeteorMessageBox.Show("找不到要刪除的模板", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error)
            End If
        Catch ex As Exception
            MeteorMessageBox.Show($"刪除失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub BtnOk_Click(sender As Object, e As RoutedEventArgs)
        Dim selected = TryCast(TemplateGrid.SelectedItem, TemplateItemVM)
        If selected IsNot Nothing Then
            SelectedSampleFileName = selected.FileName
            SelectedSampleMeta = TemplateTrainingStore.GetTrainingSampleMeta(_groupPath, selected.FileName)
        End If
        Me.DialogResult = True
        Me.Close()
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = False
        Me.Close()
    End Sub

End Class
