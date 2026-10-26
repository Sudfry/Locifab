Option Explicit
Dim fso, log, w, d, i
Set fso = CreateObject("Scripting.FileSystemObject")
Set log = fso.OpenTextFile("C:\Users\zxyzx\Desktop\LoacalCraft\build-tmp\toc_log.txt", 2, True)
log.WriteLine "start"
Set w = CreateObject("Word.Application")
w.Visible = False
w.DisplayAlerts = 0
log.WriteLine "word " & w.Version
Set d = w.Documents.Open("C:\Users\zxyzx\Desktop\LoacalCraft\Document\main.docx", False, False)
log.WriteLine "opened pages=" & d.ComputeStatistics(2)
log.WriteLine "toc count=" & d.TablesOfContents.Count
For i = 1 To d.TablesOfContents.Count
    d.TablesOfContents(i).Update
    log.WriteLine "toc " & i & " updated"
Next
d.Repaginate
log.WriteLine "pages=" & d.ComputeStatistics(2)
log.WriteLine "words=" & d.ComputeStatistics(0)
d.Save
log.WriteLine "saved"
d.Close 0
w.Quit
log.WriteLine "done"
log.Close
