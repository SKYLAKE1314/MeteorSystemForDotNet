Imports System.Threading
Imports MetroSystemForDotNet.IoBoard

Public Class DIScanf
    Implements IDisposable

    Private ReadOnly _ioCard As ModbusBuzzer
    Private ReadOnly _diIndex As Byte

    Private _cts As CancellationTokenSource
    Private _lastState As Boolean

    Public Delegate Sub ButtonChangedHandler(state As Boolean)
    Public Event OnButtonChanged As ButtonChangedHandler

    Public Sub New(ioCard As ModbusBuzzer, diIndex As Byte)
        _ioCard = ioCard
        _diIndex = diIndex
    End Sub

    Public Sub Start()

        _cts = New CancellationTokenSource()
        Dim token = _cts.Token

        Task.Run(Async Function()

                     While Not token.IsCancellationRequested

                         Try
                             Dim state = False

                             Try
                                 state = _ioCard.ReadDI(_diIndex)
                             Catch
                                 state = False
                             End Try

                             If state <> _lastState Then
                                 _lastState = state
                                 RaiseEvent OnButtonChanged(state)
                             End If

                         Catch ex As Exception
                         End Try

                         Await Task.Delay(50, token)

                     End While

                 End Function, token)

    End Sub

    Public Sub [Stop]()
        Try
            _cts?.Cancel()
        Catch
        End Try

        _cts?.Dispose()
        _cts = Nothing
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        [Stop]()
    End Sub

End Class