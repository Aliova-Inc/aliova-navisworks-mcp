# Fabrique l'installateur : un seul fichier .exe a envoyer, paquet embarque.
#
# Enchainement :
#   1. compile le plugin en Release ;
#   2. assemble le paquet d'application Autodesk (PackageContents.xml + Contents\2026\...) ;
#   3. le compresse en payload\bundle.zip ;
#   4. compile l'installateur, qui embarque ce zip en ressource.
#
# Le paquet est assemble ICI et pas a la main : la disposition des fichiers est imposee par
# Navisworks (icones a plat a cote de la DLL, XAML du ruban dans un sous-dossier de langue) et
# une erreur de chemin ne produit aucune erreur au chargement, juste un onglet muet. Le script
# verifie donc la disposition avant de compresser.

param(
    [switch]$SkipPluginBuild
)

$ErrorActionPreference = "Stop"

$InstallerDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot     = Resolve-Path (Join-Path $InstallerDir "..")
$PluginProj   = Join-Path $RepoRoot "NavisworksMcpPlugin\plugin\NavisworksMcpPlugin.csproj"
$PluginOut    = Join-Path $RepoRoot "NavisworksMcpPlugin\plugin\bin\Release\net48"
$Manifest     = Join-Path $RepoRoot "bundle\PackageContents.xml"
$SetupProj    = Join-Path $InstallerDir "AliovaMcpSetup\AliovaMcpSetup.csproj"
$Staging      = Join-Path $InstallerDir "obj-bundle"
$PayloadDir   = Join-Path $InstallerDir "AliovaMcpSetup\payload"
$PayloadZip   = Join-Path $PayloadDir "bundle.zip"

# L'installateur atterrit a la RACINE du depot, pas dans un sous-dossier. Il etait dans
# dist\, ou personne ne pense a regarder : on ouvre le dossier, on voit des dossiers de code
# et aucun executable, et on ne sait pas quoi lancer. Le seul fichier a double-cliquer doit
# etre le premier qu'on voit.
$Dist         = $RepoRoot

function Step($text) { Write-Host "" ; Write-Host "== $text ==" -ForegroundColor Cyan }

# Les dependances gerees que le plugin resout depuis son propre dossier (voir
# DependencyResolver). Elles voyagent DANS le paquet : c'est ce qui supprime le besoin
# d'ecrire dans Program Files, et donc les droits administrateur.
$Dependencies = @(
    "Microsoft.Bcl.AsyncInterfaces.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encodings.Web.dll",
    "System.Text.Json.dll",
    "System.Threading.Tasks.Extensions.dll",
    "System.ValueTuple.dll"
)

# ---------------------------------------------------------------- 1. plugin
if (-not $SkipPluginBuild) {
    Step "Compilation du plugin"
    dotnet build $PluginProj -c Release -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Compilation du plugin echouee." }
}

if (-not (Test-Path (Join-Path $PluginOut "NavisworksMcpPlugin.dll"))) {
    throw "DLL du plugin introuvable : $PluginOut"
}

# ---------------------------------------------------------------- 2. paquet
Step "Assemblage du paquet d'application"

if (Test-Path $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
$Contents = Join-Path $Staging "Contents\2026"
New-Item -ItemType Directory -Force -Path $Contents | Out-Null

Copy-Item -LiteralPath $Manifest -Destination (Join-Path $Staging "PackageContents.xml") -Force

# la DLL du plugin
Copy-Item -LiteralPath (Join-Path $PluginOut "NavisworksMcpPlugin.dll") -Destination $Contents -Force
Write-Host "  NavisworksMcpPlugin.dll"

# les dependances gerees
foreach ($dep in $Dependencies) {
    $src = Join-Path $PluginOut $dep
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination $Contents -Force
        Write-Host "  $dep"
    }
    else {
        Write-Warning "Dependance absente de la sortie de compilation : $dep"
    }
}

# les icones, a PLAT a cote de la DLL : c'est la seule place ou Icon / LargeIcon de
# l'attribut [Command] sait les resoudre
Get-ChildItem -Path $PluginOut -Filter "*.ico" -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $Contents -Force
    Write-Host ("  " + $_.Name)
}

# la mise en page du ruban, dans son sous-dossier de langue
Get-ChildItem -Path $PluginOut -Directory |
    Where-Object { Get-ChildItem -Path $_.FullName -Filter "*.xaml" -File -ErrorAction SilentlyContinue } |
    ForEach-Object {
        $dst = Join-Path $Contents $_.Name
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Get-ChildItem -Path $_.FullName -Filter "*.xaml" -File | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
            Write-Host ("  " + (Split-Path -Leaf $dst) + "\" + $_.Name)
        }
    }

# ---------------------------------------------------------------- 3. verification
Step "Verification de la disposition"

$checks = @(
    @{ Name = "manifeste du paquet";    Test = { Test-Path (Join-Path $Staging "PackageContents.xml") } },
    @{ Name = "DLL du plugin";          Test = { Test-Path (Join-Path $Contents "NavisworksMcpPlugin.dll") } },
    @{ Name = "System.Text.Json";       Test = { Test-Path (Join-Path $Contents "System.Text.Json.dll") } },
    @{ Name = "icones du ruban (>= 6)"; Test = { (Get-ChildItem $Contents -Filter "*.ico" -File).Count -ge 6 } },
    @{ Name = "XAML du ruban en langue";Test = { (Get-ChildItem $Contents -Directory | ForEach-Object { Get-ChildItem $_.FullName -Filter "*.xaml" -File }).Count -ge 1 } }
)

$failed = 0
foreach ($c in $checks) {
    if (& $c.Test) { Write-Host ("  OK       " + $c.Name) }
    else { Write-Host ("  MANQUANT " + $c.Name) -ForegroundColor Red ; $failed++ }
}
if ($failed -gt 0) { throw "$failed verification(s) en echec : le paquet ne serait pas fonctionnel." }

# Les icones nommees dans le code doivent exister : un nom faux donne un bouton sans image,
# sans aucune erreur au chargement. On relit donc les attributs de l'assembly compilee.
#
# La lecture se fait dans un PROCESSUS SEPARE, volontairement : charger une assembly par
# reflexion la verrouille pour toute la duree du processus, et le dossier de fabrication
# devenait alors impossible a nettoyer. Le verrou meurt avec le processus enfant.
$navisApi = Get-ChildItem "C:\Program Files\Autodesk\Navisworks * 2026\Autodesk.Navisworks.Api.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($navisApi) {
    $checkScript = @'
param($ApiDll, $PluginDll, $ContentsDir)
[void][System.Reflection.Assembly]::ReflectionOnlyLoadFrom($ApiDll)
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($PluginDll)
$types = @()
try { $types = $asm.GetTypes() } catch { $types = $_.Exception.InnerException.Types | Where-Object { $_ -ne $null } }
$ribbon = $types | Where-Object { $_.Name -eq "AliovaRibbonPlugin" }
if (-not $ribbon) { Write-Output "SKIP"; exit 0 }
$missing = @()
foreach ($a in [System.Reflection.CustomAttributeData]::GetCustomAttributes($ribbon)) {
    if ($a.AttributeType.Name -ne "CommandAttribute") { continue }
    foreach ($na in $a.NamedArguments) {
        if ($na.MemberName -ne "Icon" -and $na.MemberName -ne "LargeIcon") { continue }
        if (-not (Test-Path (Join-Path $ContentsDir $na.TypedValue.Value))) { $missing += $na.TypedValue.Value }
    }
}
if ($missing.Count -gt 0) { Write-Output ("MISSING:" + ($missing -join ",")) } else { Write-Output "OK" }
'@
    $tmp = Join-Path $env:TEMP ("aliova_icon_check_" + $PID + ".ps1")
    Set-Content -LiteralPath $tmp -Value $checkScript -Encoding UTF8
    try {
        $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $tmp `
            $navisApi.FullName (Join-Path $Contents "NavisworksMcpPlugin.dll") $Contents
        $verdict = ($result | Select-Object -Last 1)
        if ($verdict -like "MISSING:*") {
            Write-Host ("  MANQUANT icones nommees dans le code : " + $verdict.Substring(8)) -ForegroundColor Red
            throw "Des icones nommees dans le code sont absentes du paquet."
        }
        if ($verdict -eq "OK") { Write-Host "  OK       icones nommees dans le code presentes" }
        else { Write-Host "  (verification des icones ignoree : classe de ruban introuvable)" }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- 4. zip embarque
Step "Compression du paquet"

New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null
if (Test-Path -LiteralPath $PayloadZip) { Remove-Item -LiteralPath $PayloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $Staging, $PayloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ("  payload\bundle.zip   " + [int]((Get-Item $PayloadZip).Length / 1024) + " Ko")

# ---------------------------------------------------------------- 5. installateur
Step "Compilation de l'installateur"

dotnet build $SetupProj -c Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Compilation de l'installateur echouee." }

$setupOut = Join-Path $InstallerDir "AliovaMcpSetup\bin\Release\net48"
$exe = Get-ChildItem $setupOut -Filter "*.exe" | Select-Object -First 1
if (-not $exe) { throw "Executable de l'installateur introuvable dans $setupOut" }

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
$final = Join-Path $Dist $exe.Name
Copy-Item -LiteralPath $exe.FullName -Destination $final -Force

Step "Termine"
Write-Host ("Installateur : " + $final) -ForegroundColor Yellow
Write-Host ("Taille       : " + [int]((Get-Item $final).Length / 1024) + " Ko")
Write-Host ""
Write-Host "Un seul fichier a envoyer. Aucun droit administrateur, sauf pour retirer une"
Write-Host "ancienne installation posee dans le repertoire de Navisworks."
