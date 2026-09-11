IF OBJECT_ID(N'dbo.AuthenticationSessions', N'U') IS NOT NULL
BEGIN
    ;WITH ActiveSessions AS
    (
        SELECT AuthenticationSessionId,
               ROW_NUMBER() OVER
               (
                   PARTITION BY TenantId,UserId
                   ORDER BY LastSeenAt DESC,IssuedAt DESC,AuthenticationSessionId DESC
               ) AS Position
        FROM dbo.AuthenticationSessions
        WHERE Status=N'Active'
    )
    UPDATE session
    SET Status=N'Revoked',
        RevokedAt=SYSDATETIMEOFFSET(),
        RevocationReason=N'ExclusiveLoginCutover',
        UpdatedAt=SYSDATETIMEOFFSET()
    FROM dbo.AuthenticationSessions session
    INNER JOIN ActiveSessions active
      ON active.AuthenticationSessionId=session.AuthenticationSessionId
    WHERE active.Position>1;

    /* El índice pertenece al modelo del DACPAC. Aquí solo se normalizan los
       datos para la nueva unicidad; SQLPackage reemplaza la definición
       anterior sin una segunda eliminación durante el predeployment. */
END

IF OBJECT_ID(N'dbo.WorkSessions', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT business.TenantId,session.BusinessId,session.UserId,session.DeviceId
        FROM dbo.WorkSessions session
        INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
        WHERE session.Status=N'Open'
        GROUP BY business.TenantId,session.BusinessId,session.UserId,session.DeviceId
        HAVING COUNT_BIG(1)>1
    )
        THROW 51000,N'Hay más de una WorkSession abierta para el mismo contexto web o equipo enrolado; deben cerrarse operativamente antes del despliegue.',1;

    /* Web y cada equipo enrolado mantienen sesiones independientes. Aquí solo
       se rechazan duplicados del mismo contexto operativo; los índices
       definitivos pertenecen al modelo del DACPAC. */
END
