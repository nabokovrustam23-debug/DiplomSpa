namespace BarbershopCrm.Infrastructure.Email;

/// <summary>
/// Plain-text email bodies for account-related transactions. Matches the muted, calm tone
/// of the «Тихий час» brand.
/// </summary>
public static class AccountEmails
{
    public static EmailMessage EmailVerification(string to, string fullName, string link, int hoursValid)
    {
        var body = $$"""
            Здравствуйте, {{fullName}}.

            Мы получили запрос на регистрацию в CRM «Тихий час».
            Чтобы подтвердить адрес почты, перейдите по ссылке:

            {{link}}

            Ссылка действительна {{hoursValid}} ч.

            Если это не вы — просто проигнорируйте это письмо.

            — «Тихий час», барбершопы Краснодара.
            """;
        return new EmailMessage(to, "Подтверждение адреса почты", body);
    }

    public static EmailMessage PasswordReset(string to, string fullName, string link, int hoursValid)
    {
        var body = $$"""
            Здравствуйте, {{fullName}}.

            Мы получили запрос на восстановление пароля в CRM «Тихий час».
            Чтобы задать новый пароль, перейдите по ссылке:

            {{link}}

            Ссылка действительна {{hoursValid}} ч.

            Если запроса не было — пароль менять не нужно. Можно просто проигнорировать
            это письмо. На всякий случай рекомендуем проверить активные сессии.

            — «Тихий час», барбершопы Краснодара.
            """;
        return new EmailMessage(to, "Восстановление пароля", body);
    }
}
