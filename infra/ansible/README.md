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

## Enabling web push

Push notifications for news (ADR-017) stay off until the VAPID keys are in the vault:

```bash
npx web-push generate-vapid-keys
ansible-vault edit group_vars/amsterfam_dev/vault.yml
# add vault_push_vapid_public_key, vault_push_vapid_private_key and
# vault_push_subject (mailto:…), then redeploy:
ansible-playbook deploy.yml --ask-vault-pass
```

Generate the keys once and keep them: new keys break every existing subscription, and members
would have to turn push on again on each device.

## Enabling email

News by email (ADR-018) stays off until an SMTP server is in the vault:

```bash
ansible-vault edit group_vars/amsterfam_dev/vault.yml
# add vault_smtp_host, vault_smtp_username, vault_smtp_password and
# vault_smtp_from ("Amsterfam <news@example.com>"); vault_smtp_port and
# vault_smtp_starttls default to 587 and true. Then redeploy:
ansible-playbook deploy.yml --ask-vault-pass
```

Members then find an email switch on their profile page.

## Notes

- No firewall role — the host sits behind an external firewall already.
- Docker is installed via the `geerlingguy.docker` Galaxy role, not a hand-rolled apt role.
- All app-level operations (git, docker compose, backups) run as the unprivileged `deploy`
  user; root is only used for host bootstrap (package installs, user/ssh hardening).
- After the first successful deploy, set Authentik's public URL/branding in its own System
  Settings UI at `https://a.dev.amsterfam.eu` (not scripted here), and update the
  Terraform-managed Discord OAuth source's redirect URI to the public domain
  (`infra/terraform/authentik`).
