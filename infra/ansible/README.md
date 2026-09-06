# Ansible: dev.amsterfam.eu

Reproducible provisioning + deploy for the `dev.amsterfam.eu` host (Debian 13).

## One-time setup

```bash
ansible-galaxy install -r requirements.yml
cp group_vars/amsterfam_dev/vault.yml.example group_vars/amsterfam_dev/vault.yml
# fill in real values in vault.yml, then:
ansible-vault encrypt group_vars/amsterfam_dev/vault.yml
```

Keep the vault password out of the repo (password manager, or `ANSIBLE_VAULT_PASSWORD_FILE`
pointing at a local file outside the working tree).

Fill in `git_repo_url` in `group_vars/all.yml` with the real GitHub remote before the first run.

## First run against a fresh host

The host has no `deploy` user yet, so override the connection user for this run only:

```bash
ansible-playbook site.yml -e bootstrap_user=root --ask-vault-pass
```

This will pause partway through and print a generated SSH public key — add it to the
GitHub repo as a read-only deploy key (Settings → Deploy keys) before continuing, so
`app_deploy`'s `git clone` can succeed.

Before this run disables password SSH auth, open a **second** terminal and confirm you
can log in as `deploy` with its key — don't close your root session until that's verified.

## Subsequent runs

```bash
# fast redeploy (git pull, rebuild, compose up) — also what CI runs
ansible-playbook deploy.yml --ask-vault-pass

# full re-apply (idempotent; re-checks hardening, docker, etc.)
ansible-playbook site.yml --ask-vault-pass
```

## Notes

- No firewall role — the host sits behind an external firewall already.
- Docker is installed via the `geerlingguy.docker` Galaxy role, not a hand-rolled apt role.
- All app-level operations (git, docker compose, backups) run as the unprivileged `deploy`
  user; root is only used for host bootstrap (package installs, user/ssh hardening).
- After the first successful deploy, set Authentik's public URL/branding in its own System
  Settings UI at `https://a.dev.amsterfam.eu` (not scripted here), and update the
  Terraform-managed Discord OAuth source's redirect URI to the public domain
  (`infra/terraform/authentik`).
