# Проверка клиента на Windows

Раннер: Microsoft Windows NT 10.0.26100.0, PowerShell 5.1.26100.33648, пользователь runneradmin, интерактивная сессия: True.
Экраны: 1024x768.

### Окно-«сцена» с текстом (как окно игры)
  картинка на экране: {
    "y":  41,
    "w":  980,
    "h":  240,
    "x":  18
}

  скриншот `01-scene.png` (1024x768)
**удалось**

### Запуск клиента и вход через окно входа
  окно входа: «EasyLang — вход»
  скриншот `02-login.png` (1024x768)
  пароль набран через SendInput: событий 64 из 64
  скриншот `03-after-login.png` (1024x768)
**удалось**

### Хоткей Ctrl+Shift+E через SendInput, когда активна «сцена»
  SetForegroundWindow: True, активное окно: «Harbor Lights»
  SendInput принял событий: 6 из 6
  рамка выбора области открылась: 0,0,1024,768
  скриншот `04-selection-overlay.png` (1024x768)
**удалось**

### Выбор области мышью поверх стоп-кадра
  тянем от (14, 37) до (1002, 285)
  окно результата: «EasyLang — Harbor Lights»
  распознанные слова: Are you gonna spill the beans No cap He counts the register
  скриншот `05-result.png` (1024x768)
**удалось**

### Клик по словам (Shift+клик — фраза) и «Разобрать»
  фраза в поле: «spill the beans»
  ответ в окне:
```
spill the beans  /spɪl ðə biːnz/
To reveal a secret; to disclose.

• (verb) To reveal a secret; to disclose.
   «They had planned it as a surprise party, but somebody spilled the beans.»

Добавлено в словарь.
```
  скриншот `06-explained.png` (1024x768)
**удалось**

Итого шагов с ошибкой: 0.
