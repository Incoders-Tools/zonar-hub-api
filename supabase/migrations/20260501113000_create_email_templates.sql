-- System-level email templates editable by system administrators.
CREATE TABLE IF NOT EXISTS public.email_templates (
    id UUID PRIMARY KEY,
    key TEXT NOT NULL CHECK (char_length(key) BETWEEN 3 AND 120),
    subject TEXT NOT NULL CHECK (char_length(subject) BETWEEN 1 AND 300),
    html_body TEXT NOT NULL,
    description TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_email_templates_key ON public.email_templates(key);
CREATE INDEX IF NOT EXISTS ix_email_templates_is_active ON public.email_templates(is_active);

ALTER TABLE public.email_templates ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access on email_templates" ON public.email_templates;
CREATE POLICY "Service role full access on email_templates" ON public.email_templates
    TO service_role
    USING (true)
    WITH CHECK (true);

INSERT INTO public.email_templates (
    id,
    key,
    subject,
    html_body,
    description,
    is_active,
    created_at_utc,
    updated_at_utc)
VALUES
    (
        '4f3fd0a3-80ab-4109-ad8a-eeab5762cb41',
        'auth.verification_code',
        'Tu codigo de verificacion - ZonarHub',
        '<h2>Verificacion de cuenta</h2><p>Tu codigo es:</p><h1 style="letter-spacing:4px">{{code}}</h1><p>Valido por {{expires_minutes}} minutos.</p>',
        'Codigo de verificacion enviado durante registro.',
        TRUE,
        NOW(),
        NOW()),
    (
        '7be98ec6-af7e-44bb-8d33-55f6ec7db21d',
        'auth.password_reset',
        'Restablecer contrasena - ZonarHub',
        '<h2>Restablecer contrasena</h2><p>Recibimos una solicitud para restablecer tu contrasena.</p><p><a href="{{reset_link}}">Haz clic aqui para restablecer tu contrasena</a></p><p>El enlace es valido por {{expires_hours}} hora(s).</p><p>Si no solicitaste este cambio, puedes ignorar este email.</p>',
        'Recuperacion de contrasena para usuarios existentes.',
        TRUE,
        NOW(),
        NOW()),
    (
        '81fd80fe-0dfa-4d3a-b746-d88f02590f1f',
        'auth.welcome',
        'Bienvenido a ZonarHub',
        '<h2>Bienvenido, {{full_name}}!</h2><p>Tu cuenta fue creada exitosamente.</p>',
        'Bienvenida luego del registro exitoso.',
        TRUE,
        NOW(),
        NOW()),
    (
        '4a87e5d2-7cd6-4936-b412-37072bd4c3ce',
        'auth.invite_user',
        'Te invitaron a ZonarHub',
        '<h2>Invitacion a ZonarHub</h2><p>Recibiste una invitacion para unirte al sistema.</p><p><a href="{{invite_link}}">Aceptar invitacion</a></p>',
        'Invitacion de usuario administrada por sistema.',
        TRUE,
        NOW(),
        NOW()),
    (
        'b3e8fbc8-c244-451f-92f1-b24c519f9484',
        'auth.magic_link',
        'Acceso rapido a ZonarHub',
        '<h2>Acceso con enlace magico</h2><p>Usa el siguiente enlace para ingresar:</p><p><a href="{{magic_link}}">Ingresar ahora</a></p>',
        'Plantilla para flujos de magic link.',
        TRUE,
        NOW(),
        NOW()),
    (
        'c2f3d4cf-4902-4532-8170-7728f0437fad',
        'auth.change_email',
        'Confirmacion de cambio de email',
        '<h2>Cambio de email</h2><p>Confirmamos que solicitaste cambiar tu correo.</p><p>Codigo: <strong>{{code}}</strong></p>',
        'Plantilla para confirmar cambio de correo.',
        TRUE,
        NOW(),
        NOW()),
    (
        '7d95ab14-38f4-446d-b73f-2f7f327d95c2',
        'auth.reauthentication',
        'Verificacion de seguridad',
        '<h2>Reautenticacion requerida</h2><p>Por seguridad, confirma tu identidad con este codigo:</p><h1>{{code}}</h1>',
        'Plantilla para reautenticacion en acciones sensibles.',
        TRUE,
        NOW(),
        NOW())
ON CONFLICT (key) DO UPDATE
SET
    subject = EXCLUDED.subject,
    html_body = EXCLUDED.html_body,
    description = EXCLUDED.description,
    is_active = EXCLUDED.is_active,
    updated_at_utc = NOW();
