param(
    [Parameter(Mandatory)][string]$Current,
    [ValidateSet('Patch','Minor','Major')][string]$Bump='Patch'
)
$ErrorActionPreference='Stop'
if ($Current -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Build versions must use major.minor.patch without a prerelease suffix.'
}
$value=[version]$Current
switch ($Bump) {
    'Patch' { '{0}.{1}.{2}' -f $value.Major,$value.Minor,($value.Build+1) }
    'Minor' { '{0}.{1}.0' -f $value.Major,($value.Minor+1) }
    'Major' { '{0}.0.0' -f ($value.Major+1) }
}
