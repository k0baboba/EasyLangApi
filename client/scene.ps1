Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object System.Windows.Forms.Form
$f.Text = 'Harbor Lights'
$f.StartPosition = 'Manual'
$f.Location = New-Object System.Drawing.Point(0, 0)
$f.ClientSize = New-Object System.Drawing.Size(1000, 260)
$f.BackColor = [System.Drawing.Color]::White
$pb = New-Object System.Windows.Forms.PictureBox
$pb.Image = [System.Drawing.Image]::FromFile('D:\a\EasyLangApi\EasyLangApi\sample.png')
$pb.SizeMode = 'Zoom'
$pb.Location = New-Object System.Drawing.Point(10, 10)
$pb.Size = New-Object System.Drawing.Size(980, 240)
$f.Controls.Add($pb)
$f.Add_Shown({ $r = $pb.RectangleToScreen($pb.ClientRectangle); @{ x = $r.X; y = $r.Y; w = $r.Width; h = $r.Height } | ConvertTo-Json | Set-Content 'D:\a\EasyLangApi\EasyLangApi\out\client\scene-rect.json' })
[System.Windows.Forms.Application]::Run($f)
