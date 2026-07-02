Imports System.Diagnostics
Imports System.Globalization
Imports System.Linq
Imports System.Windows
Imports System.Windows.Controls
Imports Microsoft.VisualBasic

Class ModelEditPage

    Private ReadOnly _templateRoot As String =
        IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates")

    Private _allTemplates As New List(Of TemplateEntry)
    Private _currentView As New List(Of TemplateEntry)
    Private _isInitializing As Boolean = True

    Public Sub New()
        InitializeComponent()
        AddHandler LanguageManager.LanguageChanged, AddressOf LanguageChanged_Handler
        AddHandler Me.Unloaded, AddressOf ModelEditPage_Unloaded
        _isInitializing = False
        AddHandler Me.Loaded, AddressOf ModelEditPage_Loaded
    End Sub

    Private Sub ModelEditPage_Loaded(sender As Object, e As RoutedEventArgs)
        _isInitializing = True
        CbSort.SelectedIndex = If(String.Equals(My.Settings.ModelEditSortMode, "phonetic", StringComparison.OrdinalIgnoreCase), 1, 0)
        _isInitializing = False
        RefreshLanguageUI()
        ReloadTemplateList()
    End Sub

    Private Sub BtnRefresh_Click(sender As Object, e As RoutedEventArgs)
        ReloadTemplateList()
    End Sub

    Private Sub TbSearch_TextChanged(sender As Object, e As RoutedEventArgs)
        If _isInitializing Then Return
        ApplyFilterAndSort()
    End Sub

    Private Sub CbSort_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If _isInitializing Then Return
        My.Settings.ModelEditSortMode = If(CbSort.SelectedIndex = 1, "phonetic", "alpha")
        My.Settings.Save()
        UpdateSortIndicator()
        ApplyFilterAndSort()
    End Sub

    Private Sub ReloadTemplateList()
        Try
            If Not IO.Directory.Exists(_templateRoot) Then
                IO.Directory.CreateDirectory(_templateRoot)
            End If

            _allTemplates =
                IO.Directory.GetDirectories(_templateRoot).
                Select(Function(path) BuildTemplateEntry(path)).
                Where(Function(x) x IsNot Nothing).
                ToList()

            ApplyFilterAndSort()
        Catch ex As Exception
            MessageBox.Show("載入模板列表失敗: " & ex.Message)
        End Try
    End Sub

    Private Function BuildTemplateEntry(groupPath As String) As TemplateEntry
        Dim groupName = IO.Path.GetFileName(groupPath)
        If String.IsNullOrWhiteSpace(groupName) Then Return Nothing

        Dim camDirs =
            IO.Directory.GetDirectories(groupPath, "cam*").
            Where(Function(d) IO.File.Exists(IO.Path.Combine(d, "template.png"))).
            ToList()

        Dim hasLegacyTemplate = IO.File.Exists(IO.Path.Combine(groupPath, "template.png"))

        If camDirs.Count = 0 AndAlso Not hasLegacyTemplate Then
            Return Nothing
        End If

        Dim cameraCount = If(camDirs.Count > 0, camDirs.Count, 1)

        Dim lastWrite As DateTime =
            If(camDirs.Count > 0,
               camDirs.Select(Function(d) IO.Directory.GetLastWriteTime(d)).DefaultIfEmpty(IO.Directory.GetLastWriteTime(groupPath)).Max(),
               IO.Directory.GetLastWriteTime(groupPath))

        Return New TemplateEntry With {
            .Name = groupName,
            .FullPath = groupPath,
            .CameraSummary = $"{LanguageManager.T("ModelEdit_CameraCount")}：{cameraCount}",
            .LastUpdated = $"{LanguageManager.T("ModelEdit_LastUpdated")}：{lastWrite:yyyy-MM-dd HH:mm:ss}",
            .TrainText = LanguageManager.T("ModelEdit_Train"),
            .ReviseText = LanguageManager.T("ModelEdit_Revise"),
            .DeleteText = LanguageManager.T("ModelEdit_Delete"),
            .OpenFolderText = LanguageManager.T("ModelEdit_OpenFolder")
        }
    End Function

    Private Sub ApplyFilterAndSort()
        If _isInitializing Then Return
        If LvTemplate Is Nothing Then Return

        Dim query = If(TbSearch?.Text, "").Trim()

        Dim source = If(_allTemplates, New List(Of TemplateEntry)())
        Dim data = source.Where(Function(x) x IsNot Nothing).AsEnumerable()

        If query.Length > 0 Then
            data = data.Where(Function(x)
                                  Return x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                              End Function)
        End If

        Dim sortIndex = If(CbSort?.SelectedIndex, 0)
        If sortIndex = 1 Then
            Dim twComparer = StringComparer.Create(New CultureInfo("zh-TW"), ignoreCase:=True)
            data = data.OrderBy(Function(x) x.Name, twComparer)
        Else
            data = data.OrderBy(Function(x) x.Name, StringComparer.OrdinalIgnoreCase)
        End If

        _currentView = data.ToList()
        LvTemplate.ItemsSource = _currentView
        RefreshSortIndex(_currentView)
    End Sub

    Private Sub RefreshSortIndex(items As List(Of TemplateEntry))
        If LvSortIndex Is Nothing Then Return

        Dim isPhonetic = (CbSort.SelectedIndex = 1)
        Dim labels As IEnumerable(Of String)

        If isPhonetic Then
            labels = New String() {"ㄅ", "ㄆ", "ㄇ", "ㄈ", "ㄉ", "ㄊ", "ㄋ", "ㄌ", "ㄍ", "ㄎ", "ㄏ", "ㄐ", "ㄑ", "ㄒ", "ㄓ", "ㄔ", "ㄕ", "ㄖ", "ㄗ", "ㄘ", "ㄙ"}
        Else
            labels = Enumerable.Range(AscW("A"c), 26).Select(Function(i) ChrW(i).ToString())
        End If

        Dim list As New List(Of SortIndexEntry)
        For Each l In labels
            If items.Any(Function(x) GetSortLabel(x.Name, isPhonetic) = l) Then
                list.Add(New SortIndexEntry With {.Label = l})
            End If
        Next

        If items.Any(Function(x) GetSortLabel(x.Name, isPhonetic) = "#") Then
            list.Add(New SortIndexEntry With {.Label = "#"})
        End If

        LvSortIndex.DisplayMemberPath = "Label"
        LvSortIndex.ItemsSource = list
    End Sub

    Private Function GetSortLabel(name As String, phoneticMode As Boolean) As String
        If String.IsNullOrWhiteSpace(name) Then Return "#"

        Dim c = name.Trim()(0)
        Dim u = Char.ToUpperInvariant(c)

        If u >= "A"c AndAlso u <= "Z"c Then
            Return u.ToString()
        End If

        If phoneticMode Then
            Dim code = AscW(c)
            If code >= AscW("ㄅ"c) AndAlso code <= AscW("ㄩ"c) Then
                Return c.ToString()
            End If
        End If

        Return "#"
    End Function

    Private Sub LvSortIndex_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Dim entry = TryCast(LvSortIndex.SelectedItem, SortIndexEntry)
        If entry Is Nothing Then Return

        Dim isPhonetic = (CbSort.SelectedIndex = 1)
        Dim target = _currentView.FirstOrDefault(Function(x) GetSortLabel(x.Name, isPhonetic) = entry.Label)
        If target IsNot Nothing Then
            LvTemplate.ScrollIntoView(target)
        End If

        LvSortIndex.SelectedItem = Nothing
    End Sub

    Private Sub BtnRevise_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn Is Nothing Then Return

        Dim oldPath = TryCast(btn.Tag, String)
        If String.IsNullOrWhiteSpace(oldPath) OrElse Not IO.Directory.Exists(oldPath) Then Return

        Dim oldName = IO.Path.GetFileName(oldPath)
        Dim newName = Interaction.InputBox(
            LanguageManager.T("ModelEdit_RenamePrompt"),
            LanguageManager.T("ModelEdit_RenameTitle"),
            oldName)

        If String.IsNullOrWhiteSpace(newName) Then Return

        For Each c In IO.Path.GetInvalidFileNameChars()
            newName = newName.Replace(c, "_"c)
        Next

        If String.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase) Then Return

        Dim newPath = IO.Path.Combine(IO.Path.GetDirectoryName(oldPath), newName)

        If IO.Directory.Exists(newPath) Then
            MessageBox.Show(LanguageManager.T("ModelEdit_ErrorNameExists"))
            Return
        End If

        Try
            IO.Directory.Move(oldPath, newPath)
            UpdateLastTemplatePathAfterMove(oldPath, newPath)
            ReloadTemplateList()
        Catch ex As Exception
            MessageBox.Show(LanguageManager.T("ModelEdit_ErrorRevise") & ": " & ex.Message)
        End Try
    End Sub

    Private Sub BtnTrain_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn Is Nothing Then Return

        Dim groupPath = TryCast(btn.Tag, String)
        If String.IsNullOrWhiteSpace(groupPath) OrElse Not IO.Directory.Exists(groupPath) Then Return

        Try
            Dim dlg As New TemplateTrainDialog(groupPath)
            dlg.Owner = Application.Current?.MainWindow
            dlg.ShowDialog()
            ReloadTemplateList()
        Catch ex As Exception
            MessageBox.Show(LanguageManager.T("ModelEdit_ErrorTrain") & ": " & ex.Message)
        End Try
    End Sub

    Private Sub BtnDelete_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn Is Nothing Then Return

        Dim targetPath = TryCast(btn.Tag, String)
        If String.IsNullOrWhiteSpace(targetPath) OrElse Not IO.Directory.Exists(targetPath) Then Return

        Dim result = MessageBox.Show(
            $"{LanguageManager.T("ModelEdit_DeleteConfirm")}{Environment.NewLine}{targetPath}",
            LanguageManager.T("ModelEdit_DeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning)

        If result <> MessageBoxResult.Yes Then Return

        Try
            IO.Directory.Delete(targetPath, recursive:=True)
            ClearLastTemplateIfDeleted(targetPath)
            ReloadTemplateList()
        Catch ex As Exception
            MessageBox.Show(LanguageManager.T("ModelEdit_ErrorDelete") & ": " & ex.Message)
        End Try
    End Sub

    Private Sub BtnOpenFolder_Click(sender As Object, e As RoutedEventArgs)
        Dim btn = TryCast(sender, Button)
        If btn Is Nothing Then Return

        Dim targetPath = TryCast(btn.Tag, String)
        If String.IsNullOrWhiteSpace(targetPath) OrElse Not IO.Directory.Exists(targetPath) Then Return

        Try
            Process.Start("explorer.exe", targetPath)
        Catch ex As Exception
            MessageBox.Show(LanguageManager.T("ModelEdit_ErrorOpenFolder") & ": " & ex.Message)
        End Try
    End Sub

    Private Sub RefreshLanguageUI()
        TxtTitle.Text = LanguageManager.T("ModelEdit_Title")
        TbSearch.PlaceholderText = LanguageManager.T("ModelEdit_SearchPlaceholder")
        BtnRefresh.Content = LanguageManager.T("ModelEdit_Refresh")
        If CbSort.Items.Count >= 2 Then
            CType(CbSort.Items(0), ComboBoxItem).Content = LanguageManager.T("ModelEdit_SortAlpha")
            CType(CbSort.Items(1), ComboBoxItem).Content = LanguageManager.T("ModelEdit_SortPhonetic")
        End If
        UpdateSortIndicator()
    End Sub

    Private Sub UpdateSortIndicator()
        Dim sortKey = If(CbSort.SelectedIndex = 1, "ModelEdit_SortPhonetic", "ModelEdit_SortAlpha")
        TxtSortIndicator.Text = $"{LanguageManager.T("ModelEdit_CurrentSort")}：{LanguageManager.T(sortKey)}"
    End Sub

    Private Sub LanguageChanged_Handler(sender As Object, e As EventArgs)
        RefreshLanguageUI()
        ReloadTemplateList()
    End Sub

    Private Sub ModelEditPage_Unloaded(sender As Object, e As RoutedEventArgs)
        RemoveHandler LanguageManager.LanguageChanged, AddressOf LanguageChanged_Handler
        RemoveHandler Me.Unloaded, AddressOf ModelEditPage_Unloaded
    End Sub

    Private Sub UpdateLastTemplatePathAfterMove(oldRoot As String, newRoot As String)
        Dim lastPath = LastTemplateStore.Load()
        If String.IsNullOrWhiteSpace(lastPath) Then Return

        If lastPath.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase) Then
            Dim suffix = lastPath.Substring(oldRoot.Length)
            LastTemplateStore.Save(newRoot & suffix)
        End If
    End Sub

    Private Sub ClearLastTemplateIfDeleted(deletedRoot As String)
        Dim lastPath = LastTemplateStore.Load()
        If String.IsNullOrWhiteSpace(lastPath) Then Return

        If lastPath.StartsWith(deletedRoot, StringComparison.OrdinalIgnoreCase) Then
            LastTemplateStore.Save("")
        End If
    End Sub

    Private Class TemplateEntry
        Public Property Name As String
        Public Property FullPath As String
        Public Property CameraSummary As String
        Public Property LastUpdated As String
        Public Property TrainText As String
        Public Property ReviseText As String
        Public Property DeleteText As String
        Public Property OpenFolderText As String
    End Class

    Private Class SortIndexEntry
        Public Property Label As String
    End Class

End Class
