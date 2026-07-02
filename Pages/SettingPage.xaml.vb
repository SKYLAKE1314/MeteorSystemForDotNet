Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel
Imports System.Collections.ObjectModel

Public Class SettingPage

    Private _isLoaded As Boolean = False

    Private _ioMode As IoBoardMode = IoBoardMode.NONE
    Private _cameraList As New List(Of CameraInfo)

    Public Property CameraRows As New ObservableCollection(Of CameraRow)

    Public Sub New()
        InitializeComponent()

        CameraRows = New ObservableCollection(Of CameraRow)

        Me.DataContext = Me   ' ⭐ 讀settings

        _isLoaded = False
        _ioMode = IoBoardModeHelper.Parse(My.Settings.IoMode)
        AppRuntime.IoMode = _ioMode

        LoadCameraRows()

        Select Case _ioMode
            Case IoBoardMode.IO
                IoBoardComboBox.SelectedIndex = 0
            Case IoBoardMode.PLC
                IoBoardComboBox.SelectedIndex = 1
            Case Else
                IoBoardComboBox.SelectedIndex = 2
        End Select

        _isLoaded = True

        AddHandler Me.Loaded, AddressOf SettingPage_Loaded
    End Sub

    ' =========================
    ' Page Loaded
    ' =========================
    Private Sub SettingPage_Loaded(
    sender As Object,
    e As RoutedEventArgs)

        _isLoaded = True

        Select Case LanguageManager.CurrentLanguage
            Case "zhTW"
                LanguageComboBox.SelectedIndex = 0
            Case "zhCN"
                LanguageComboBox.SelectedIndex = 1
            Case "enUS"
                LanguageComboBox.SelectedIndex = 2
            Case Else
                LanguageComboBox.SelectedIndex = 0
        End Select

        LoadCameraRows()

        Dim savedId As String = My.Settings.CameraDeviceId
    End Sub

    ' =========================
    ' Language Switch
    ' =========================
    Private Sub LanguageComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If DesignerProperties.GetIsInDesignMode(Me) Then Return
        If Not _isLoaded Then Return

        Select Case LanguageComboBox.SelectedIndex
            Case 0 : LanguageManager.Load("zhTW")
                Logger.Debug("正體中文")
            Case 1 : LanguageManager.Load("zhCN")
            Case 2 : LanguageManager.Load("enUS")
        End Select

        Dim main = TryCast(Application.Current?.MainWindow, MainWindow)
        If main IsNot Nothing Then
            main.RefreshLanguageUI()
        End If


    End Sub
    ' 板卡

    Private Sub IoBoardComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return
        If IoBoardComboBox.SelectedItem Is Nothing Then Return

        Select Case IoBoardComboBox.SelectedIndex
            Case 0
                _ioMode = IoBoardMode.IO
            Case 1
                _ioMode = IoBoardMode.PLC
            Case Else
                _ioMode = IoBoardMode.NONE
        End Select

        My.Settings.IoMode = _ioMode.ToString()
        My.Settings.Save()
        AppRuntime.IoMode = _ioMode

        ' 自動化
        AutoRun.SelectedIndex =
If(My.Settings.AutoRun, 0, 1)

    End Sub

    Private Sub Camera_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return

        Dim cb = DirectCast(sender, ComboBox)
        Dim row = DirectCast(cb.DataContext, CameraRow)

        Dim cam = TryCast(cb.SelectedItem, CameraInfo)
        row.SelectedCamera = cam

        RefreshAllCameraLists()
        SaveCameraRows()

        My.Settings.CameraDeviceId = cam?.DeviceId
        My.Settings.Save()

    End Sub

    Private Sub LoadCameraRows()

        Dim list = CameraManager.GetCachedCameras()

        CameraRows.Clear()

        Dim savedIds = My.Settings.CameraDeviceIds

        If savedIds IsNot Nothing AndAlso savedIds.Count > 0 Then

            For Each id In savedIds

                Dim camRow = New CameraRow With {
                .Title = $"相機 {CameraRows.Count + 1}",
                .CameraList = list
            }

                camRow.SelectedCamera =
                list.FirstOrDefault(Function(c) c.DeviceId = id)

                CameraRows.Add(camRow)
            Next

        Else
            ' fallback
            CameraRows.Add(New CameraRow With {
            .Title = "相機 1",
            .CameraList = list
        })
        End If

        UpdateCameraButtons()
        RefreshAllCameraLists()

        ' 自動化
        If My.Settings.AutoRun Then
            AutoRun.SelectedIndex = 0      'True
        Else
            AutoRun.SelectedIndex = 1      'False
        End If

    End Sub

    Private Function GetAvailableCameras(currentRow As CameraRow) As List(Of CameraInfo)

        Dim all = CameraManager.GetCachedCameras()

        Dim used = CameraRows.
        Where(Function(r) r.SelectedCamera IsNot Nothing AndAlso r IsNot currentRow).
        Select(Function(r) r.SelectedCamera.DeviceId).
        ToList()

        Return all.
        Where(Function(c) Not used.Contains(c.DeviceId)).
        ToList()

    End Function

    Private Sub UpdateCameraButtons()

        For i = 0 To CameraRows.Count - 1

            CameraRows(i).Title = $"相機 {i + 1}"

            ' ➕ 永遠只在最後一個
            CameraRows(i).AddVisible =
            If(i = CameraRows.Count - 1,
               Visibility.Visible,
               Visibility.Collapsed)

            ' ➖ 第一個永遠不能刪
            CameraRows(i).RemoveVisible =
            If(i = 0,
               Visibility.Collapsed,
               Visibility.Visible)

        Next

    End Sub
    ' 新增相機
    Private Sub AddCamera()
        Dim list = CameraManager.GetCachedCameras()

        CameraRows.Add(New CameraRow With {
        .Title = $"相機 {CameraRows.Count + 1}",
        .CameraList = list
    })

        UpdateCameraButtons()
        SaveCameraRows()
    End Sub

    ' 相機控件刪減
    Private Sub AddCamera_Click(sender As Object, e As RoutedEventArgs)
        AddCamera()
    End Sub

    Private Sub RemoveCamera_Click(sender As Object, e As RoutedEventArgs)

        Dim btn = DirectCast(sender, Button)
        Dim row = TryCast(btn.DataContext, CameraRow)

        If row Is Nothing Then Return

        RemoveCamera(row)

    End Sub

    Private Sub RemoveCamera(row As CameraRow)

        If CameraRows.Count <= 1 Then Return

        CameraRows.Remove(row)

        UpdateCameraButtons()
        SaveCameraRows()
    End Sub

    Private Sub SaveCameraRows()
        Dim ids As New System.Collections.Specialized.StringCollection()

        For Each row In CameraRows
            If row.SelectedCamera IsNot Nothing Then
                ids.Add(row.SelectedCamera.DeviceId)
            End If
        Next

        My.Settings.CameraDeviceIds = ids
        My.Settings.Save()
    End Sub

    Private Sub RefreshAllCameraLists()

        For Each row In CameraRows
            row.CameraList = GetAvailableCameras(row)
        Next

    End Sub

    Private Sub AutoRunComboBox_SelectionChanged(
    sender As Object,
    e As SelectionChangedEventArgs)

        If Not _isLoaded Then Return
        Select Case AutoRun.SelectedIndex
            Case 0
                My.Settings.AutoRun = True
            Case Else
                My.Settings.AutoRun = False
        End Select

        My.Settings.Save()

    End Sub

    Private Sub GpuBoostComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)

    End Sub
End Class
