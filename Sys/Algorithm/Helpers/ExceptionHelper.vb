Imports System.Windows

Public Class ExceptionHelper

    Public Shared Sub ShowError(ex As Exception)

        Try

            MeteorMessageBox.Show(
                ex.Message,
                "錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Warning)

        Catch

        End Try

    End Sub

End Class