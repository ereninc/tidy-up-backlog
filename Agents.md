# Unity Project Instructions

This is a Unity project.

## Context efficiency

Treat generated Unity directories as out of scope unless explicitly required:

- Library/
- Temp/
- Logs/
- obj/
- .vs/

Do not scan or search these directories.

Start investigation from the files, classes, components, prefabs, or systems named in the task.

Prefer targeted searches for:
- class/type names
- method names
- serialized field names
- interfaces
- event subscriptions
- direct usages/references

Do not build a complete repository map before making a change unless the task specifically requires an architecture-wide analysis.

Do not repeatedly inspect unchanged files.

## Existing architecture

Follow the project's existing architecture and coding conventions.

Before creating:
- a new manager
- singleton
- service
- helper
- utility
- interface
- abstraction

check whether an existing system already serves that responsibility.

Prefer extending an existing appropriate system over introducing a parallel implementation.

Do not refactor unrelated code while fixing a bug.

Do not rename public APIs or serialized fields unless necessary for the requested task.

Preserve serialized data compatibility whenever reasonably possible.

## Unity assets and serialization

Never modify or regenerate .meta GUIDs unless explicitly required.

Do not manually invent Unity object-reference GUIDs.

Be very careful when directly editing:
- .unity
- .prefab
- .asset
- .meta

For creating or modifying ScriptableObjects, prefabs, or serialized Unity object references in bulk, prefer a Unity Editor utility using APIs such as:

- AssetDatabase
- SerializedObject
- SerializedProperty
- PrefabUtility

instead of manually constructing YAML.

When an Editor utility is only needed to perform a one-time migration or generation task, keep it isolated and clearly identify it as temporary.

Do not put UnityEditor APIs into runtime assemblies.

## C# changes

Keep changes localized.

Do not add comments that merely restate the code.

Avoid introducing allocations into Update, FixedUpdate, or other hot paths without a reason.

Respect existing namespaces and assembly boundaries.

Do not add or change packages, asmdefs, scripting defines, or project-wide settings unless the task requires them.

When fixing multiplayer/networked code, preserve the existing authority model and explicitly inspect whether the affected operation runs on client, server, or both.

## Validation

Do not launch Unity, trigger a full project import, perform a full build, or run expensive repository-wide validation unless requested or genuinely necessary.

Use lightweight checks first.

Run only tests or validation relevant to the modified system when possible.

If final verification requires entering Play Mode or inspecting something in the Unity Editor, state exactly what should be checked instead of performing unrelated work.

## Output

Make the requested edits directly.

Do not reproduce entire changed source files in the response unless explicitly asked.

At the end, briefly report:
- files changed
- important behavior changes
- any Unity Editor / Play Mode verification that remains