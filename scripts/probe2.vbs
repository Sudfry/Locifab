Option Explicit
Dim fso, log, w, d
Set fso = CreateObject("Scripting.FileSystemObject")
Set log = fso.OpenTextFile("C:\Users\zxyzx\Desktop\LoacalCraft\build-tmp\toc_log.txt", 2, True)
On Error Resume Next
Set w = CreateObject("Word.Application")
If Err.Number <> 0 Then
  log.WriteLine "create fail " & Err.Number & " " & Err.Description
  log.Close
  WScript.Quit
End If
w.Visible = False
w.DisplayAlerts = 0
w.AutomationSecurity = 3
Set d = w.Documents.Open("C:\Users\zxyzx\Desktop\LoacalCraft\Document\main.docx", False, False)
If Err.Number <> 0 Then
  log.WriteLine "open fail " & Err.Number & " " & Err.Description
Else
  log.WriteLine "opened pages=" & d.ComputeStatistics(2)
End If
log.Close
