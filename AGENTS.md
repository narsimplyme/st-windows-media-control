# Repository instructions

## Commit attribution

For commits containing work authored or co-authored by Codex, append this trailer
once, separated from the commit body by a blank line:

```text
Co-authored-by: Codex <noreply@openai.com>
```

Keep the user's configured Git author and committer identity. Do not add this
trailer to human-only work. Apply this preference to future commits; do not
rewrite published history solely to add attribution unless the user requests it.

## Planning and review

Use the codex-with-chatgpt skill for future coding tasks in this repository, as
requested by the user. ChatGPT provides planning and review; Codex performs edits
and tests. Follow the skill's connection and workspace-verification flow. If the
connection needs user input or is unavailable, report that rather than silently
switching to a Codex-only implementation. Do not send private agent configuration,
credentials, or certificates to ChatGPT.
