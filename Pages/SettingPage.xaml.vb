Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel

Public Class SettingPage

    Private _isLoaded As Boolean = False

    Public Sub New()
        InitializeComponent()

        AddHandler Me.Loaded, AddressOf SettingPage_Loaded
    End Sub

    ' =========================
    ' Page Loaded
    ' =========================
    Private Sub SettingPage_Loaded(sender As Object, e As RoutedEventArgs)
        _isLoaded = True
    End Sub

    ' =========================
    ' Language Switch
    ' =========================
    Private Sub LanguageComboBox_SelectionChanged(
        sender As Object,
        e As SelectionChangedEventArgs)

        '  避免設計器觸發
        If DesignerProperties.GetIsInDesignMode(Me) Then Return

        '  避免初始化時觸發
        If Not _isLoaded Then Return

        Dim combo = TryCast(sender, ComboBox)
        If combo Is Nothing Then Return

        Select Case combo.SelectedIndex

            Case 0
                LanguageManager.Load("zhTW")

            Case 1
                LanguageManager.Load("zhCN")

            Case 2
                LanguageManager.Load("enUS")

        End Select

        ' =========================
        ' 安全更新 MainWindow UI
        ' =========================
        Dim main = TryCast(Application.Current?.MainWindow, MainWindow)

        If main IsNot Nothing Then
            main.RefreshLanguageUI()
        End If

    End Sub

End Class