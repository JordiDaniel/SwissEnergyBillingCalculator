# SwissEnergyBillingCalculator

SwissEnergyBillingCalculator ist ein Tool zur Verarbeitung von ebIX oder csv-Dateien für Schweizer Zusammenschlüsse zum Eigenverbrauch (ZEV) und virtuelle Zusammenschlüsse zum Eigenverbrauch (vZEV).
Das Projekt unterstützt die automatische Verarbeitung von Energiedaten, um Abrechnungen innerhalb eines ZEV/vZEV effizient und nachvollziehbar erstellen zu können.

## Funktionen

- Liest Files aus einem Share aus (dazu kann ein n8n Workflow gebaut werden um die Dateien automatisch abzulegen)
- Verarbeitung von Lastgang- und Verbrauchsdaten
- Unterstützung von ZEV- und vZEV-Abrechnungen
- Automatische Berechnung von Energiebezügen und Kostenanteilen
- Nachvollziehbare und transparente Abrechnung

## Anwendungsfälle

### (v)ZEV-Abrechnung

Das Tool ermöglicht die Verarbeitung der vom Netzbetreiber gelieferten ebIX/csv-Dateien und die Aufteilung der Energiekosten auf die einzelnen Teilnehmer eines (v)ZEV.
Im Tool können Liegenschaften/Parteien dynamisch gebildet werden.

## Voraussetzungen

- Home Assistant
- Optional: n8n von https://github.com/Rbillon59/hass-n8n

## Installation

https://www.home-assistant.io/common-tasks/os/apps/#adding-an-app-repository
