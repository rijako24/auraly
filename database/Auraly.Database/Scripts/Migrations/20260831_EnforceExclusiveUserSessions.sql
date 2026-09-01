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
        SELECT UserId
        FROM dbo.WorkSessions
        WHERE Status=N'Open'
        GROUP BY UserId
        HAVING COUNT_BIG(1)>1
    )
        THROW 51000,N'Hay usuarios con más de una WorkSession abierta; deben cerrarse operativamente antes del despliegue.',1;

    /* El índice pertenece al modelo del DACPAC. Aquí solo se valida el dato;
       SQLPackage reemplaza la definición antigua sin una segunda eliminación. */
END
