$ErrorActionPreference = 'Stop'
# Original GitCompass teaching content. This script makes the declared inventory reproducible.
$entries = [System.Collections.Generic.List[object]]::new()
function Add-Lesson($id, $title, $purpose, $prerequisites, $effects, $pause, $example, $outcome, $practice) {
    $entries.Add([ordered]@{ Id=$id; Title=$title; Category='Daily workflow'; Purpose=$purpose; Prerequisites=$prerequisites; Effects=$effects; WhenToPause=$pause; Example=$example; Outcome=$outcome; Practice=$practice })
}
$entries.Add([ordered]@{ Id='basics'; Title='Start here: files, saved versions, and online sharing'; Category='Getting started'; Purpose='Your project is a folder of files. Git remembers versions of selected files on this computer, so you can see how your work changed. GitHub can host and share those versions online; it is optional for local work.'; Prerequisites='No Git or GitHub knowledge is needed. Choose your own project or use a separate practice folder.'; Effects='Reading this lesson changes nothing. Saving a file in your editor changes that file. Saving a version with Git records selected contents locally. Uploading sends saved versions to a reviewed destination.'; WhenToPause='Pause when you do not recognize the project, the selected files, or the upload destination. The walkthrough explains each change before asking you to review it.'; Example='Edit and save a file → Choose files for the version → Describe the change → Review and save locally → Share online when ready'; Outcome='You can explain the difference between a changed file, a version saved on this computer, and a version uploaded for others.'; Practice='Start the walkthrough and choose "Start the practice project". Make one small change and save one local version. No account or upload is required.' })
Add-Lesson 'journey' 'From a changed file to an uploaded version' 'Your work goes through four steps: you edit a file, choose it for saving (stage), save a version on this computer (commit), and send it online (push). Save the file in your editor first, so GitCompass can see the change.' 'A practice folder, Git installed, and Allow changes turned on for that folder.' 'Choosing files (stage) only marks them for the next save. Saving a version (commit) keeps a snapshot on this computer. Uploading (push) sends saved versions to the online copy you picked. Edits you did not choose, or did not save in your editor, are not included.' 'Stop if files you did not expect are chosen, you are on the wrong line of work (branch), you have not checked for updates in a while, or a step was left unfinished.' "git status`ngit diff --no-ext-diff --no-textconv`ngit add -- practice.txt`ngit diff --cached --no-ext-diff --no-textconv`ngit commit -m 'Explain the first practice change'`ngit push <reviewed-remote> HEAD:<reviewed-branch>" 'A saved version can stay safely on your computer before you upload it. GitCompass only says it was uploaded after checking the online copy.' 'Choose Create a separate practice folder, start saving versions there, edit practice.txt, choose only that file, save one version, and look at the history.'
Add-Lesson 'ignore' 'Choose what belongs in Git' 'Ignore rules tell Git to skip some files, like build output or private settings. They only affect files Git is not saving yet. A file already saved stays saved, and so do its old versions.' 'Look at the files in your project and decide which ones should never be saved, for example passwords or large generated files.' 'Adding a rule edits a small file called .gitignore. That file can be saved too. Removing an already-saved file from Git is a separate step that keeps the file on your computer.' 'Stop if you see passwords, keys, .env files or very large files. An ignore rule cannot hide a secret that was already uploaded.' 'git check-ignore -v -- build/output.txt' 'Git shows which rule matches when a file is ignored. A file that is already saved stays saved.' 'In the practice folder, create build/output.txt, preview an ignore rule, and check which files it affects before adding it.'
Add-Lesson 'pr' 'A review request (pull request)' 'A review request (pull request) asks your team on GitHub to look at your uploaded line of work (branch) and add it to theirs (merge). It is different from Get updates (pull), which downloads new work.' 'Your work is uploaded (pushed), you are signed in to GitHub, and you are allowed to change the project. Know which line of work you want to add to.' 'Making a request creates a page on GitHub for your team to review. It does not combine anything yet, and it does not upload files you have not saved.' 'Stop if your work is not uploaded yet, you are not sure which project or line of work to target, or an earlier request might already exist. Check for an existing request before trying again.' 'Source: your-account:feature/example → Target: team/project main' 'A request appears on GitHub for review. Whether it can be combined may show as pending at first; that does not mean there is a problem.' 'Read this lesson offline. Only make a real request in a practice project your team set up for it.'
Add-Lesson 'conflicts' 'Fix clashing changes (merge conflict)' 'A clash (conflict) happens when you and someone else changed the same lines. Git keeps both versions in the file and asks you to choose. Neither side is automatically right.' 'A combine (merge) that stopped with a clash, the list of files with clashes, your work saved first, and a text editor.' 'Edit each file so it reads the way you want, delete the <<<<<<< ======= >>>>>>> marker lines, choose the fixed file for saving (stage), then finish the combine. Cancelling (abort) tries to put things back as they were.' 'Stop for pictures, renamed or deleted files, or big clashes, and ask a developer. Always picking one side can throw away work you wanted. Cancelling is not a full backup.' "git status`ngit diff --no-ext-diff --no-textconv`n# Resolve a practice text file in an editor, then review:`ngit add -- practice.txt`ngit commit" 'The combine is finished only when every clash is fixed and the combined version is saved, or the cancel is done.' 'Use two practice copies and a practice online copy. Change the same line in both, then combine them and fix the clash.'
Add-Lesson 'remote' 'Online copies (remotes) and where uploads go' 'An online copy (remote) is another copy of your project, usually on GitHub. It often has the short name origin. Your line of work (branch) can follow one online line of work for updates, and uploads may go to a different one, for example your own copy (fork).' 'The web address of the online copy, given to you by your team, and the line of work you are on.' 'Looking at these settings changes nothing. Adding an online copy or choosing what to follow changes Git settings for this project, so GitCompass asks you first.' 'Stop if there are several upload places, nothing is being followed, or the online copy has unrelated history. Do not assume every project uses origin and main.' "git remote -v`ngit branch -vv" 'You can see each online copy and which line of work each of yours follows. If GitCompass cannot tell, it says unknown rather than zero.' 'Use practice online copies and compare the one you get updates from with the one you upload to.'
$rows = @'
add|Select file contents for the next commit|Writes the index; does not create a commit or upload anything.|git add -- practice.txt
am|Apply a mailbox patch series as commits|Writes files, index, and local commits; conflicts can interrupt the series.|git am --show-current-patch
annotate|Attribute recorded file lines to commits|Reads history; line attribution is context rather than proof of sole authorship.|git annotate -- practice.txt
apply|Apply a patch to files or the index|May change files or the index; check the patch before applying.|git apply --check practice.patch
archive|Export a recorded tree into an archive|Writes the selected archive file; excludes uncommitted work.|git archive --format=zip --output=practice.zip HEAD
backfill|Retrieve missing objects in a partial clone|Downloads objects and changes repository object storage.|git backfill -h
bisect|Narrow down which commit introduced a regression|Changes checkout state during the search; record your starting branch first.|git bisect -h
blame|See which recorded commit last changed each line|Reads history; later edits and copied code can complicate attribution.|git blame -- practice.txt
branch|List or manage named lines of work|Listing reads refs; create/delete/rename options change local branch refs.|git branch -vv
bugreport|Prepare Git environment information for support|Writes a report file that may contain private environment details; review it.|git bugreport -h
bundle|Package selected Git history for offline transfer|Writes a bundle file; does not capture uncommitted or ignored content.|git bundle -h
cat-file|Inspect stored Git objects|Reads object contents; contents may contain private source data.|git cat-file -t HEAD
check-attr|Inspect configured attributes for a path|Reads attribute rules used for content handling; does not edit files.|git check-attr -a -- practice.txt
check-ignore|Explain why an untracked path is ignored|Reads ignore rules; an existing tracked file remains tracked.|git check-ignore -v -- build/output.txt
check-mailmap|Resolve author identities through mailmap rules|Reads mapping rules; does not rewrite history.|git check-mailmap -h
check-ref-format|Validate a branch or ref name|Reads the supplied name; no repository effect.|git check-ref-format --branch feature/practice
checkout|Select a branch or restore selected contents|May overwrite files or detach HEAD; prefer explicit switch/restore intent.|git checkout -h
checkout-index|Copy index contents into working files|Writes files and may overwrite edits; advanced index operation.|git checkout-index -h
cherry|Compare patch-equivalent commits between histories|Reads history; equivalent patches can still have different commit identities.|git cherry -h
cherry-pick|Apply chosen commits onto the current branch|Writes files, index, and new commits; may leave a conflicted operation.|git cherry-pick -h
clean|Remove untracked working files|Deletion can be permanent; dry-run and review every path first.|git clean -n
clone|Create a separate working copy of a repository|Writes a new destination and may access a remote. Never clone over an old ZIP copy.|git clone <authorized-repository> <new-empty-folder>
column|Format text into display columns|Formats input; has no Git history effect.|git column -h
commit|Record the reviewed index as a local version|Creates a local commit; runs configured hooks/signing and does not upload it.|git commit -m 'Describe the reviewed change'
commit-graph|Manage Git history acceleration data|Writes repository maintenance metadata; not a visual graph command.|git commit-graph -h
commit-tree|Create a commit object from an explicit tree|Writes an object; does not automatically move a branch. Advanced plumbing.|git commit-tree -h
config|Read or set Git configuration|Reads by default for queries; setters change local/global/system policy and can affect executable helpers.|git config --get user.name
count-objects|Inspect repository object storage|Reads storage statistics; numbers do not describe pending uploads.|git count-objects -v
credential|Ask configured credential helpers for transport credentials|Can read or store credential material; never copy its secret output into support reports.|git credential -h
credential-cache|Keep transport credentials temporarily in memory|May start a credential helper; keep secrets out of logs.|git credential-cache -h
credential-store|Store transport credentials in a file|May write plaintext credentials; understand the credential helper policy first.|git credential-store -h
daemon|Serve repositories through the Git protocol|Starts a network service; requires explicit exposure and access policy.|git daemon -h
describe|Name a commit using nearby tags|Reads history and tags; the description is not a branch permission claim.|git describe --always HEAD
diff|Inspect changes between files, index, or commits|Reads differences when inspection drivers are disabled. A difference can intentionally use a nonzero exit code.|git diff --no-ext-diff --no-textconv
diff-files|Compare working files with the index|Reads worktree/index differences; advanced machine-oriented output.|git diff-files --name-status
diff-index|Compare a tree with the index or working files|Reads differences; choose cached mode deliberately for index contents.|git diff-index --cached --name-status HEAD
diff-tree|Compare recorded trees|Reads committed changes; merge options alter which parents are compared.|git diff-tree --name-status HEAD
difftool|Open a configured external diff viewer|Can start configured executable tools; trust the repository and viewer configuration.|git difftool -h
fast-export|Export Git history in a machine transfer format|Writes export output; may expose source content and identities.|git fast-export -h
fast-import|Import machine-described objects and refs|Can write extensive objects and branch history; advanced migration workflow.|git fast-import -h
fetch|Refresh local knowledge of remote history|Downloads objects and updates permitted tracking metadata. It does not update your checked-out files.|git fetch <reviewed-remote>
fetch-pack|Retrieve objects directly from another repository|Writes objects; advanced transport plumbing with explicit targets.|git fetch-pack -h
filter-branch|Rewrite recorded history using filters|Changes commit identities and refs; published history must not be rewritten casually.|git filter-branch -h
fmt-merge-msg|Format a merge commit message|Formats merge context; does not complete a merge itself.|git fmt-merge-msg -h
for-each-ref|List refs using a chosen machine format|Reads branch/tag/remote references.|git for-each-ref --format='%(refname) %(objectname)'
for-each-repo|Run a selected Git command for configured repositories|Effects depend on the invoked command and each target; inspect the repository list.|git for-each-repo -h
format-patch|Export commits as reviewable email-style patches|Writes patch files; they can contain private source and author details.|git format-patch -h
fsck|Check stored object connectivity and validity|Inspects repository integrity; reporting missing objects is not a safe generic reset instruction.|git fsck --no-reflogs
gc|Perform repository cleanup and optimization|Writes maintenance data and may prune unreachable objects; understand retention first.|git gc -h
get-tar-commit-id|Extract a commit identifier from a Git tar archive|Reads archive input; no repository write.|git get-tar-commit-id -h
grep|Search tracked or selected recorded contents|Reads matching source text; output may be private.|git grep -- 'example'
hash-object|Compute or write an object identifier for file contents|Without -w reads contents; -w writes to the object database.|git hash-object -- practice.txt
help|Read installed Git help or list commands|Reads command information; version-specific options should be verified locally.|git help -a
hook|Run a named configured Git hook|Starts configured code and can have arbitrary effects; explicit trust is required.|git hook -h
http-backend|Serve repository transport through an HTTP server|Starts backend processing under server policy; advanced hosting.|git http-backend -h
http-fetch|Retrieve objects using HTTP transport|Downloads repository objects; advanced transport command.|git http-fetch -h
http-push|Send objects using an HTTP transport|Can change a remote; use reviewed ordinary push for daily workflows.|git http-push -h
imap-send|Send prepared patch mail to an IMAP folder|Transmits message content and uses credentials; requires explicit authorization.|git imap-send -h
index-pack|Build a searchable index for packed objects|Writes repository pack metadata; advanced object transfer.|git index-pack -h
init|Initialize Git in an explicitly chosen folder|Creates Git metadata. It does not upload files or manufacture previous history.|git init --initial-branch=main <practice-folder>
interpret-trailers|Read or format structured message trailers|May rewrite selected message files with options; review authorship/signoff meaning.|git interpret-trailers -h
log|Read recorded versions and their relationships|Reads commits, parents, and messages. Uncommitted edits are not historical commits.|git log --oneline --decorate --graph -20
ls-files|List index and working-file classifications|Reads file path metadata; matching flags determine tracked/untracked/ignored groups.|git ls-files
ls-remote|Read refs advertised by a remote repository|Contacts the remote; does not refresh local remote-tracking refs.|git ls-remote <reviewed-remote>
ls-tree|Inspect files recorded in a tree|Reads recorded paths and object IDs; ignores working-file edits.|git ls-tree HEAD
mailinfo|Extract a message and patch from email input|Writes requested output files; inspect untrusted patch content.|git mailinfo -h
mailsplit|Split mailbox input into individual messages|Writes message files; no branch change by itself.|git mailsplit -h
maintenance|Run or configure repository maintenance|Writes optimization data or scheduled configuration; requires deliberate policy.|git maintenance -h
merge|Combine reviewed histories with their common ancestor|May write files, index, and a merge commit. Conflicts require manual decisions.|git merge --ff-only --no-overwrite-ignore <reviewed-commit>
merge-base|Find common ancestry or relationship evidence|Reads commit relationships; unrelated or incomplete history must be explicit.|git merge-base -h
merge-file|Merge selected file contents|Can overwrite a selected file or produce conflicted output; use copies for practice.|git merge-file -h
merge-index|Invoke a merge program for index entries|Starts a configured merge program and writes selected contents; advanced conflict processing.|git merge-index -h
merge-tree|Inspect a merge of committed snapshots|Modern preview may write objects even without changing worktree/index; it is not strict read-only.|git merge-tree -h
mergetool|Open configured external conflict-resolution tools|Starts tools that edit conflicted files and may change the index.|git mergetool -h
mktag|Create a validated tag object from input|Writes an object; advanced tagging plumbing.|git mktag -h
mktree|Create a tree object from an explicit listing|Writes a tree object; does not stage files or move a branch.|git mktree -h
multi-pack-index|Manage indexes across packed object files|Writes repository maintenance metadata.|git multi-pack-index -h
mv|Move a tracked path and update the index|Changes files and staging; inspect the resulting diff.|git mv -- old-practice.txt practice.txt
name-rev|Describe objects using reachable ref names|Reads history; names can change when refs move.|git name-rev HEAD
notes|Attach or inspect separate commit notes|Read commands inspect notes; add/edit commands change notes refs and are not ordinary commit contents.|git notes list
pack-objects|Create packed storage for selected objects|Writes packed object data; advanced storage/transport command.|git pack-objects -h
pack-refs|Pack loose ref storage|Changes metadata representation without intending a history change.|git pack-refs -h
patch-id|Calculate a comparison identity for a patch|Reads patch input; equivalence does not prove identical commit IDs.|git patch-id -h
prune|Remove unreachable stored objects|May make unreferenced work unrecoverable; understand retention and dry-run first.|git prune -n
prune-packed|Remove loose copies of already packed objects|Changes object storage; advanced maintenance.|git prune-packed -n
pull|Fetch and integrate history into the current branch|Can change files and branch history. Explicit --ff-only stops when histories diverge.|git pull --ff-only <reviewed-remote> <reviewed-branch>
push|Publish recorded commits to a reviewed destination|Changes remote refs. It excludes unstaged files, editor buffers, and ordinary local stashes.|git push <reviewed-remote> HEAD:<reviewed-branch>
range-diff|Compare two commit series|Reads patch relationships; useful after deliberate rebasing without equating identities.|git range-diff -h
read-tree|Load tree contents into the index|Writes index and optionally working files; advanced index operation.|git read-tree -h
rebase|Replay work onto a chosen base|Changes commit identities and can alter files. Do not casually rebase shared history.|git rebase -h
receive-pack|Accept a Git push on a server|Writes server objects and refs under server policy; not a daily client command.|git receive-pack -h
reflog|Inspect or manage recorded local ref movements|Inspection helps identify local previous states; expiry/deletion affects recovery records.|git reflog -20
refs|Inspect or maintain the repository ref backend|Effects depend on the subcommand; migrations write repository metadata.|git refs -h
remote|Inspect or change named repository connections|Listing reads configuration; add/set-url/remove/set-head can write configuration or refs.|git remote -v
repack|Reorganize packed object storage|Writes repository maintenance data; options can remove older packs.|git repack -h
replace|Substitute object relationships locally|Writes replacement refs and can change how history is interpreted.|git replace -l
replay|Replay commits onto a specified base|Creates rewritten commit objects/refs; advanced controlled workflow.|git replay -h
request-pull|Prepare a textual request to review published history|Produces a summary, not a GitHub API pull request or automatic merge.|git request-pull -h
rerere|Record or reuse earlier conflict resolutions|May store resolution metadata or update files/index with options; review reused output.|git rerere -h
reset|Move refs or reset the index and possibly files|Effects vary by mode; --hard can discard work. It is not a generic Undo button.|git reset -h
restore|Restore index or working-file contents from a source|Can discard edits; staged and worktree effects are distinct.|git restore -h
rev-list|List reachable commits or compare ancestry|Reads commit relationships; use left/right operand order deliberately for counts.|git rev-list --left-right --count HEAD...<reviewed-upstream>
rev-parse|Resolve repository locations and revision expressions|Reads identity/capability facts; missing tracking is not a zero count.|git rev-parse --show-toplevel
revert|Create a corrective commit for a recorded change|Writes a new commit and may conflict; preserves shared history rather than rewriting it.|git revert -h
rm|Remove selected paths from the index and optionally files|Default removes files; --cached removes tracking while retaining the working copy.|git rm -h
send-pack|Send selected refs through Git transport|Can change a remote; advanced equivalent transport path, not read-only.|git send-pack -h
shortlog|Summarize recorded commits by author|Reads recorded metadata; counts do not measure individual contribution quality.|git shortlog -sn HEAD
show|Inspect a stored commit, tree, tag, or file object|Reads recorded content; output may contain private source.|git show --stat HEAD
show-branch|Compare branch histories|Reads relationships; complex output benefits from a graph and explicit labels.|git show-branch -h
show-index|Inspect a pack index file|Reads object storage metadata.|git show-index -h
show-ref|List or verify stored refs|Reads local refs, including cached remote-tracking refs.|git show-ref --head
sparse-checkout|Select which tracked paths populate the working tree|Writes sparse-checkout settings and working files; preserve local edits first.|git sparse-checkout -h
stash|Temporarily preserve unfinished local contents|Writes local stash objects/refs; apply can conflict. Include untracked files only deliberately.|git stash list
status|Distinguish staged, unstaged, new, and conflicted paths|Reads current facts. Branch comparisons use cached upstream information until fetch succeeds.|git status --short --branch
stripspace|Normalize whitespace in message input|Transforms text; has no history effect unless its output is deliberately used.|git stripspace -h
submodule|Inspect or manage nested repository relationships|Read commands inspect; update/add changes nested repositories, metadata, and possibly network state.|git submodule status
switch|Change to an explicit branch|Changes checked-out files and HEAD. Preserve local edits and check task intent first.|git switch --no-overwrite-ignore <reviewed-local-branch>
symbolic-ref|Read or set a symbolic ref|Reading HEAD inspects its branch; setters change metadata and need review.|git symbolic-ref --short HEAD
tag|Inspect or create labels for recorded commits|Listing reads refs; creation/deletion changes tags locally and may require explicit later publication.|git tag --list
unpack-file|Extract a stored blob to a temporary file|Writes a temporary file containing recorded content.|git unpack-file -h
unpack-objects|Expand a packed object stream|Writes objects; advanced transfer plumbing.|git unpack-objects -h
update-index|Modify index entries or behavior flags|Writes the index; advanced changes can hide normal inspection signals.|git update-index -h
update-ref|Change refs with explicit identity expectations|Writes branch/tag/other metadata; incorrect targets can disconnect work.|git update-ref -h
update-server-info|Prepare auxiliary data for basic server transports|Writes repository server metadata.|git update-server-info -h
upload-archive|Serve archive requests|Reads selected recorded contents and transmits them under server policy.|git upload-archive -h
upload-pack|Serve object fetch requests|Reads selected objects and transmits them under server policy.|git upload-pack -h
var|Inspect a Git logical variable|Reads configured identity/editor information; review private output before sharing.|git var GIT_AUTHOR_IDENT
verify-commit|Check commit signatures|Reads signature evidence using configured verification tools; trust and key policy matter.|git verify-commit HEAD
verify-pack|Check packed object indexes and storage|Reads repository integrity data.|git verify-pack -h
verify-tag|Check annotated tag signatures|Reads signature evidence; a signature alone does not prove company authorization.|git verify-tag -h
version|Read the installed Git version|Reads executable version information; compare capabilities against the declared supported inventory.|git version
worktree|Inspect or manage linked working directories|Listing reads ownership; add/remove/prune writes linked worktree metadata and files.|git worktree list
write-tree|Create a tree object from the current index|Writes an object; does not create a commit or move a branch.|git write-tree -h
'@
$commands = @()
foreach ($line in ($rows -split "`r?`n")) {
    if (-not $line) { continue }
    $fields = $line.Split('|'); $command = $fields[0]; $commands += $command
    $category = if ($command -in @('status','diff','add','commit','fetch','pull','push','clone','init','stash','branch','switch','merge','remote','log')) { 'Daily commands' } else { 'Advanced and plumbing reference' }
    $entries.Add([ordered]@{ Id="git-$command"; Title="git $command — $($fields[1])"; Category=$category; Purpose=$fields[1]; Prerequisites='Try it in a practice folder first. Check you are in the right project and on the right line of work (branch). The example is for learning; GitCompass does not run it for you.'; Effects=$fields[2]; WhenToPause='Stop if you are not sure what will change, if you have unsaved work, or if the change could affect your team''s online copy. Ask someone who knows Git. Reading a lesson never changes your project.'; Example=$fields[3]; Outcome='Read what Git printed, then look at your project again to check the result. A command starting does not always mean it worked.'; Practice='Try it in a separate practice folder, not on real work. For examples that upload or download, use a practice online copy. Inside GitCompass, each step still asks you first.' })
}
$manifest = [ordered]@{ SchemaVersion=1; Version='1.0.0'; SupportedCoreCommands=$commands; Entries=$entries.ToArray() }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Encoding utf8
