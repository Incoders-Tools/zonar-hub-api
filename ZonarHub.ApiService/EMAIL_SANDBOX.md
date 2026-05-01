# Email Sandbox (Mailpit)

This project can send real SMTP messages to a local sandbox inbox.

## Start Mailpit

```powershell
docker compose -f ZonarHub.ApiService/docker-compose.mailpit.yml up -d
```

Mailpit endpoints:

- SMTP: `localhost:1025`
- Web UI: `http://localhost:8025`

## Configure API

In `ZonarHub.ApiService/appsettings.Development.json` set:

- `Email:Provider = Smtp`
- `Email:Smtp:Host = localhost`
- `Email:Smtp:Port = 1025`
- `Email:Smtp:UseSsl = false`

No username/password is required for Mailpit by default.

## Verify templates

After starting API + frontend:

1. Send verification code from register flow
2. Trigger forgot password flow
3. Complete registration (welcome email)
4. Review generated messages in Mailpit Web UI

The email body/subject comes from `email_templates` (Supabase table). If a template is inactive or missing, the API falls back to a safe built-in message.
