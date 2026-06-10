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

End Class