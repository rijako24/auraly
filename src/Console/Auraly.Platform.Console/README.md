# Auraly - Aplicación de Consola

Aplicación de consola para interactuar con la IA como si los mensajes llegaran de WhatsApp. Esta aplicación utiliza los mismos servicios que la API de Azure Functions, permitiendo probar y desarrollar la lógica de negocio sin necesidad de configurar WhatsApp.

## 🚀 Configuración

1. **Configurar `appsettings.json`**:
   - Actualiza la cadena de conexión a la base de datos
   - Configura las credenciales de OpenAI
   - Configura las credenciales de WhatsApp (aunque no se usarán para enviar mensajes reales)
   - Configura Blob Storage si necesitas las imágenes de los planes

2. **Aplicar migraciones** (si aún no lo has hecho):
   ```powershell
   cd src\Infrastructure\Auraly.Platform.Infrastructure
   dotnet ef database update --startup-project ..\..\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj --context ApplicationDbContext
   ```

## 💻 Ejecutar la Aplicación

```powershell
cd src\Console\Auraly.Platform.Console
dotnet run
```

O desde la raíz del proyecto:

```powershell
dotnet run --project src/Console/Auraly.Platform.Console/Auraly.Platform.Console.csproj
```

## 📝 Uso

Una vez ejecutada la aplicación, puedes escribir mensajes como si fueras un cliente de WhatsApp:

```
Tú: Hola, quiero información sobre los planes
Tú: ¿Cuánto cuesta el plan premium?
Tú: Mi bebé tiene 4 meses
Tú: Quiero reservar
```

Para salir, escribe `exit`, `quit` o `salir`.

## 🎯 Características

- ✅ Usa los mismos servicios que la API de Azure Functions
- ✅ Procesa mensajes con la IA igual que WhatsApp
- ✅ Guarda conversaciones y leads en la base de datos
- ✅ Clasifica intenciones y genera respuestas contextuales
- ✅ Ideal para desarrollo y pruebas sin necesidad de WhatsApp

## ⚠️ Notas Importantes

- Los mensajes se procesan igual que si vinieran de WhatsApp
- Las respuestas de la IA se muestran en la consola (no se envían por WhatsApp real)
- El servicio de WhatsApp está configurado pero no enviará mensajes reales desde la consola
- Todos los mensajes se guardan en la base de datos con el número de teléfono simulado: `+1234567890`

## 🔧 Personalización

Puedes cambiar el número de teléfono y nombre del cliente en `Program.cs`:

```csharp
var userNumber = "+1234567890";
var customerName = "Usuario de Prueba";
```
