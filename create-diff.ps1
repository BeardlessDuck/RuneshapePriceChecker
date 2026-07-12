$content = Get-Content -Raw "diff.txt"
$md = "```diff`r`n" + $content + "`r`n```"
Set-Content -Path "C:\Users\joetr\.gemini\antigravity\brain\bb134e74-0386-49cb-9d7a-c8aefd17aaa8\changes.md" -Value $md
