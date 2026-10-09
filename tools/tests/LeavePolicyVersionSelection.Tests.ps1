Describe 'Stage 4 deterministic leave-policy version selection' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\LeavePolicyVersionSelection.ps1')
        function Version([string]$id, [int]$number, [string]$status, [string]$from = '2026-01-01', [string]$to = '') {
            [pscustomobject]@{ id = $id; versionNumber = $number; status = $status; effectiveFrom = $from; effectiveTo = if ($to) { $to } else { $null } }
        }
    }

    It 'resumes exactly one draft' {
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @(Version 'draft-1' 2 'Draft') })
        $result.Kind | Should Be 'Draft'
        $result.Version.id | Should Be 'draft-1'
    }

    It 'reuses exactly one published version without mutation' {
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @(Version 'pub-1' 1 'Published') })
        $result.Kind | Should Be 'Published'
        $result.Version.id | Should Be 'pub-1'
    }

    It 'selects the sole draft when published history also exists' {
        $items = @((Version 'pub-1' 1 'Published'), (Version 'draft-2' 2 'Draft'))
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items })
        $result.Kind | Should Be 'Draft'
        $result.Version.id | Should Be 'draft-2'
    }

    It 'selects an active v2 Draft when v1 is Discarded' {
        $items = @((Version 'discarded-1' 1 'Discarded'), (Version 'draft-2' 2 'Draft'))
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items })
        $result.Kind | Should Be 'Draft'
        $result.Version.id | Should Be 'draft-2'
    }

    It 'accepts Discarded history in inventory and preserves it' {
        $items = @((Version 'discarded-1' 1 'Discarded'), (Version 'discarded-2' 2 'Discarded'))
        $result = Get-LeavePolicyVersionInventory ([pscustomobject]@{ items = $items })
        $result.Classification | Should Be 'DISCARDED HISTORY'
        $result.DiscardedCount | Should Be 2
        $result.Items.Count | Should Be 2
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items }) } | Should Throw 'only Discarded history'
    }

    It 'selects Published history when Published and Discarded versions coexist' {
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @((Version 'pub-2' 2 'Published'), (Version 'discarded-1' 1 'Discarded')) })
        $result.Kind | Should Be 'Published'
        $result.Version.id | Should Be 'pub-2'
    }

    It 'selects the active Draft when Published, Draft, and Discarded coexist' {
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @((Version 'pub-1' 1 'Published'), (Version 'draft-2' 2 'Draft'), (Version 'discarded-3' 3 'Discarded')) })
        $result.Version.id | Should Be 'draft-2'
    }

    It 'stops on an unknown future status' {
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @(Version 'future-1' 4 'Archived') }) } | Should Throw 'unknown version status'
    }

    It 'stops on a null items collection rather than treating the response as empty' {
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $null }) } | Should Throw 'without a persisted version id'
    }

    It 'stops on a malformed scalar response rather than creating a version' {
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ success = $true; data = 'not-a-version-list' }) } | Should Throw 'without a persisted version id'
    }

    It 'selects the highest version-number published record from historical published versions' {
        $items = @((Version 'pub-1' 1 'Published'), (Version 'pub-2' 2 'Published'))
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items })
        $result.Version.id | Should Be 'pub-2'
        $result.PublishedHistory.Count | Should Be 2
    }

    It 'deduplicates identical persisted IDs while preserving raw diagnostics' {
        $items = @((Version 'pub-1' 1 'Published'), (Version 'pub-1' 1 'Published'))
        $result = Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items })
        $result.DistinctCount | Should Be 1
        $result.RawCount | Should Be 2
        $result.DuplicateCount | Should Be 1
    }

    It 'stops on competing drafts' {
        $items = @((Version 'draft-1' 2 'Draft' '2026-01-01'), (Version 'draft-2' 3 'Draft' '2026-02-01'))
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items }) } | Should Throw 'multiple competing Draft'
    }

    It 'stops on conflicting current published effective periods' {
        $items = @((Version 'pub-a' 4 'Published' '2026-01-01' '2026-12-31'), (Version 'pub-b' 4 'Published' '2026-06-01'))
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = $items }) } | Should Throw 'conflicting current Published'
    }

    It 'stops on null responses but permits an explicit empty item collection for first-version creation' {
        { Resolve-DeterministicLeavePolicyVersion $null } | Should Throw 'response was null'
        (Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @() })).Kind | Should Be 'None'
    }

    It 'stops on a response item without persisted identity' {
        { Resolve-DeterministicLeavePolicyVersion ([pscustomobject]@{ items = @([pscustomobject]@{ status = 'Draft' }) }) } | Should Throw 'without a persisted version id'
    }

    It 'classifies competing drafts without selecting or changing either draft' {
        $items = @((Version 'draft-1' 2 'Draft'), (Version 'draft-2' 3 'Draft'))
        $result = Get-LeavePolicyVersionInventory ([pscustomobject]@{ items = $items })
        $result.Classification | Should Be 'COMPETING DRAFTS'
        $result.DraftCount | Should Be 2
        $result.Items.Count | Should Be 2
    }

    It 'classifies published history plus one draft while retaining every persisted version' {
        $items = @((Version 'pub-1' 1 'Published'), (Version 'draft-2' 2 'Draft'))
        $result = Get-LeavePolicyVersionInventory ([pscustomobject]@{ items = $items })
        $result.Classification | Should Be 'ONE DRAFT'
        $result.Items.Count | Should Be 2
    }

    It 'leaves partial configuration to the existing inspect-before-mutate guards' {
        $runner = Get-Content (Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1') -Raw
        $runner | Should Match 'configuredTypes.Count -gt 0'
        $runner | Should Match 'matchingGroup.Count -ne 1'
        $runner | Should Match 'Invoke-LeaveConfigurationMutation'
        $runner | Should Match 'Validate deterministic leave policy version'
    }

    It 'covers completed Stage 4 resume guards' {
        $runner = Get-Content (Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1') -Raw
        $runner | Should Match 'Leave request: REUSED'
        $runner | Should Match 'Manager approval: REUSED'
        $runner | Should Match 'Stage 4 leave lifecycle: PASS'
        $runner | Should Match 'isReplay -ne \$true'
    }

    It 'does not create a version when non-empty history has no selectable version' {
        $runner = Get-Content (Join-Path $PSScriptRoot '..\Run-EmployeeLifecycleE2E.ps1') -Raw
        $runner | Should Match 'inventory was not empty but no selectable version exists'
        $runner | Should Match 'selected deterministic leave policy version is not an editable Draft'
    }
}
