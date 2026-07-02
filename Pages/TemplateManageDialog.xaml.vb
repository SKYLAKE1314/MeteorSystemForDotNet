Imports System.Windows

Public Class TemplateManageDialog

    Private _groupPath As String
    Private _templates As New List(Of TemplateItemVM)()
    Private _modified As Boolean = False

    Public Class TemplateItemVM
        Public Property Id As String
        Public Property CameraSlot As Integer
        Public Property CreatedAt As DateTime
        Public Property CreatedAtDisplay As String
        Public Property LastUsed As DateTime?
        Public Property LastUsedDisplay As String
        Public Property FilePath As String
    End Class

    Public Sub New(groupPath As String)
        InitializeComponent()
        _groupPath = TemplateTrainingStore.NormalizeGroupPath(groupPath)
        LoadTemplates()
    End Sub

    Private Sub LoadTemplates()
        _templates.Clear()

        Try
            ' List all template subdirectories under the group path
            If Not IO.Directory.Exists(_groupPath) Then Return

            For camSlot As Integer = 0 To 1
                Dim camPath = IO.Path.Combine(_groupPath, $"cam{camSlot + 1}")
                If Not IO.Directory.Exists(camPath) Then Continue For

                For Each templateDir In IO.Directory.GetDirectories(camPath)
                    Dim configPath = IO.Path.Combine(templateDir, "config.json")
                    If IO.File.Exists(configPath) Then
                        Try
                            Dim config = Newtonsoft.Json.JsonConvert.DeserializeObject(Of TemplateConfig)(
                                IO.File.ReadAllText(configPath))
                            Dim createdTime = IO.File.GetCreationTime(configPath)
                            Dim lastModTime = IO.File.GetLastWriteTime(configPath)

                            _templates.Add(New TemplateItemVM With {
                                .Id = IO.Path.GetFileName(templateDir),
                                .CameraSlot = camSlot + 1,
                                .CreatedAt = createdTime,
                                .CreatedAtDisplay = createdTime.ToString("yyyy-MM-dd HH:mm"),
                                .LastUsed = lastModTime,
                                .LastUsedDisplay = lastModTime.ToString("yyyy-MM-dd HH:mm"),
                                .FilePath = templateDir
                            })
                        Catch
                        End Try
                    End If
                Next
            Next

            ' Sort by last used time (most recent first)
            _templates = _templates.OrderByDescending(Function(x) x.LastUsed).ToList()
            TemplateGrid.ItemsSource = _templates

        Catch ex As Exception
            MessageBox.Show($"載入模板失敗：{ex.Message}")
        End Try
    End Sub

    Private Sub BtnDelete_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = CType(sender, Button)
        Dim templateId = CType(btn.Tag, String)

        Dim item = _templates.FirstOrDefault(Function(x) x.Id = templateId)
        If item Is Nothing Then Return

        Dim result = MessageBox.Show($"確定要刪除 {templateId} 嗎？", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question)
        If result <> MessageBoxResult.Yes Then Return

        Try
            ' Delete the entire template directory
            If IO.Directory.Exists(item.FilePath) Then
                IO.Directory.Delete(item.FilePath, True)
            End If

            _templates.Remove(item)
            TemplateGrid.ItemsSource = Nothing
            TemplateGrid.ItemsSource = _templates
            _modified = True

            MessageBox.Show("已刪除", "成功", MessageBoxButton.OK, MessageBoxImage.Information)
        Catch ex As Exception
            MessageBox.Show($"刪除失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub BtnOk_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = True
        Me.Close()
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = False
        Me.Close()
    End Sub

End Class
