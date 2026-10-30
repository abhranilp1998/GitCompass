# Setting up GitHub sign-in (one time)

GitCompass signs in to GitHub with the **device flow**: it shows a short code, you enter it at github.com/login/device, and GitHub returns a token. The device flow uses only a **public client ID**. No client secret is used, shipped or stored.

Without a client ID, local Git, lessons, the graph, and ordinary `git fetch`/`git push` all still work. Only the hosted GitHub features need it, such as pull requests.

## 1. Register a GitHub App (about 5 minutes)

1. Go to **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App** (https://github.com/settings/apps/new).
2. **GitHub App name:** for example `GitCompass`. **Homepage URL:** `https://github.com/abhranilp1998/GitCompass`.
3. **Callback URL:** leave it empty. Clear **Expire user authorization tokens** only if you want non-expiring tokens; GitCompass handles refresh either way.
4. Tick **Enable Device Flow**. This is required.
5. **Webhook:** clear **Active**.
6. **Permissions → Repository:** Contents: Read-only, Metadata: Read-only, Pull requests: Read and write. Add more only if a feature asks for them.
7. **Where can this GitHub App be installed?** Choose **Any account** so other users can sign in.
8. Choose **Create GitHub App**. Copy the **Client ID** (it starts with `Iv`). **Do not generate a client secret.** GitCompass doesn't need one.

## 2. Ship it with the release builds

Add a repository **variable** (it's public, so it doesn't need to be a secret): **Settings → Secrets and variables → Actions → Variables → New repository variable**
- Name: `GITCOMPASS_GITHUB_CLIENT_ID`
- Value: the Client ID from step 1

The release workflow passes this value to the build, which embeds it in `GitCompass.dll`. For a local build, set the environment variable `GITCOMPASS_GITHUB_CLIENT_ID` or run `dotnet build -p:GitCompassGitHubClientId=Iv...`.

## 3. Overriding in the app

**Settings → Advanced connection setup → Registered GitHub App public client ID**. A value saved there takes priority over the shipped one. Leave it empty to use the shipped ID.
