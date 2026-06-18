Public Class ErrorDialog

    Public Sub New(message As String)

        InitializeComponent()

        TxtMessage.Text = message

    End Sub

    Private Sub BtnOk_Click(
        sender As Object,
        e As RoutedEventArgs)

        DialogResult = True

    End Sub

End Class

Public NotInheritable Class ErrorDialogHelper

    Public Shared Sub ShowError(message As String)

        Dim dlg As New ErrorDialog(message)

        Dim owner = Window.GetWindow(Application.Current.MainWindow)

        If owner IsNot Nothing Then
            dlg.Owner = owner
        End If

        dlg.ShowDialog()

    End Sub

End Class