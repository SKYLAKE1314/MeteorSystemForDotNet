Imports System
Imports System.Threading
Imports System.Threading.Tasks
Imports IoBoard
Imports MetroSystemForDotNet.IoBoard

Namespace MetroClient.Services

    Public Class BuzzerController
        Implements IDisposable

        Private _buzzer As ModbusBuzzer
        Private ReadOnly _buzzerSem As New SemaphoreSlim(1, 1)
        Private _buzzerBeepCts As CancellationTokenSource
        Private ReadOnly _log As Action(Of String)

        Private ReadOnly _ip As String
        Private ReadOnly _port As Integer
        Private ReadOnly _unitId As Byte
        Private ReadOnly _coilBase As UShort

        Public Property Enabled As Boolean
        Public Property DurationMs As Integer = 500
        Public Property RetryTimes As Integer = 1

        Public Sub New(ip As String,
               port As Integer,
               unitId As Byte,
               coilBase As UShort,
               log As Action(Of String))

            _ip = ip
            _port = port
            _unitId = unitId
            _coilBase = coilBase

            If log Is Nothing Then
                _log = Sub(x As String)
                       End Sub
            Else
                _log = log
            End If

        End Sub

        Public Async Function InitializeAsync() As Task
            Await Task.Delay(50)

            Try
                _buzzer = New ModbusBuzzer(_ip, _port, _unitId, _coilBase)
                _buzzer.Logger = Function(s)
                                     _log("[Buzzer] " & s)
                                     Return 0
                                 End Function

                Await Task.Run(Sub() _buzzer.Connect())
                _log("信息卡初始化完成")

            Catch ex As Exception
                _log("Buzzer init failed: " & ex.Message)

                Try
                    If _buzzer IsNot Nothing Then _buzzer.Dispose()
                Catch
                End Try

                _buzzer = Nothing
            End Try
        End Function

        Public Sub TriggerIfNeeded(label As String)
            If Not String.Equals(label, "NG", StringComparison.OrdinalIgnoreCase) Then Return
            If Not Enabled Then Return

            TriggerOnceAsync(1, DurationMs)
        End Sub

        Public Async Function TriggerOnceAsync(channelOneBased As Integer,
                                               durationMs As Integer) As Task

            Await _buzzerSem.WaitAsync().ConfigureAwait(False)

            Try
                Try
                    If _buzzerBeepCts IsNot Nothing Then _buzzerBeepCts.Cancel()
                Catch
                End Try

                If _buzzerBeepCts IsNot Nothing Then
                    _buzzerBeepCts.Dispose()
                End If

                _buzzerBeepCts = New CancellationTokenSource()
                Dim ct = _buzzerBeepCts.Token

                ' lazy init
                If _buzzer Is Nothing Then
                    Try
                        _buzzer = New ModbusBuzzer(_ip, _port, _unitId, _coilBase)

                        _buzzer.Logger = Function(s)
                                             _log("[Buzzer] " & s)
                                             Return 0
                                         End Function

                        Await Task.Run(Sub() _buzzer.Connect())
                        _log("IO卡初始化succeed")

                    Catch ex As Exception
                        _log("Trigger 初始化IO卡失敗: " & ex.Message)

                        Try
                            If _buzzer IsNot Nothing Then _buzzer.Dispose()
                        Catch
                        End Try

                        _buzzer = Nothing
                        Return
                    End Try
                End If

                Try
                    Try
                        _buzzer.SetDO(channelOneBased, True)
                    Catch
                    End Try

                    Try
                        Await Task.Delay(durationMs, ct).ConfigureAwait(False)
                    Catch ex As TaskCanceledException
                    End Try

                Finally
                    Try
                        _buzzer.SetDO(channelOneBased, False)
                    Catch exOff As Exception
                        _log("嘗試關閉輸出失敗: " & exOff.Message)
                    End Try
                End Try

            Finally
                _buzzerSem.Release()
            End Try

        End Function

        Public Sub Dispose() Implements IDisposable.Dispose

            Try
                If _buzzer IsNot Nothing Then _buzzer.Dispose()
            Catch
            End Try

            Try
                If _buzzerBeepCts IsNot Nothing Then _buzzerBeepCts.Cancel()
            Catch
            End Try

            If _buzzerBeepCts IsNot Nothing Then
                _buzzerBeepCts.Dispose()
            End If

            _buzzerSem.Dispose()

        End Sub

    End Class

End Namespace