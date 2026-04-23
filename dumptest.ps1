$asm = [System.Reflection.Assembly]::LoadFrom("C:\Users\HCK PC\Downloads\aim assist\AimAssistPro\bin\Release\net8.0-windows\win-x64\Nefarius.ViGEm.Client.dll")
$type = $asm.GetType("Nefarius.ViGEm.Client.Targets.IXbox360Controller")
foreach ($m in $type.GetMethods()) {
    $params = @()
    foreach ($p in $m.GetParameters()) {
        $params += $p.ParameterType.Name
    }
    $ps = [string]::Join(", ", $params)
    Write-Output "$($m.Name)($ps)"
}
$btnType = $asm.GetType("Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360Button")
if ($btnType) {
    if ($btnType.IsEnum) {
        Write-Output "`nEnum Values for Xbox360Button:"
        foreach ($v in [Enum]::GetValues($btnType)) {
            Write-Output "$v = $([int]$v)"
        }
    } else {
        Write-Output "`nStatic Fields for Xbox360Button (not an enum):"
        foreach ($f in $btnType.GetFields()) {
            Write-Output "$($f.Name)"
        }
    }
} else {
    Write-Output "`nXbox360Button type not found!"
}
