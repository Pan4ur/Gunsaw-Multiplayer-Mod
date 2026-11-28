"use strict";

const byId = id => document.getElementById(id);
const make = (tag, text, className) => {
  const element = document.createElement(tag);
  if (text) element.textContent = text;
  if (className) element.className = className;
  return element;
};
function button(text, click, className) {
  const item = make("button", text, className);
  item.type = "button";
  item.addEventListener("click", click);
  return item;
}
function fillOptions(select, values, selected) {
  select.replaceChildren(...values.map(value => new Option(value, value, false, value === selected)));
}
const status = byId("status");
const dialog = byId("dialog");
const content = byId("dialog-content");
const dialogApply = byId("dialog-apply");
const draft = new Map();
const fields = new Map();
const controls = new Map();
const fixedInputs = { PlayerName: byId("host"), LobbyName: byId("lobby-name"), Server: byId("server") };
let state;
let fieldsLayout = "";
let playersLayout = "";
let mapsLayout = "";
let sceneLayout = "";
let working = false;
let revision = 0;
let problem = "";
let dialogCommit;

const valueFor = key => draft.get(key) ?? fields.get(key)?.value ?? "";
const isTrue = value => String(value).toLowerCase() === "true";
const pending = () => [...draft].map(([key, value]) => ({ key, value }));
function edit(key, value) {
  if (fields.get(key)?.value === value) draft.delete(key);
  else draft.set(key, value);
  renderControls();
}
function visible(field, query) {
  return (!query || [field.label, field.group, field.help].some(text => text.toLowerCase().includes(query))) &&
    field.rules.every(rule => isTrue(valueFor(rule.key)) === rule.value && visible(fields.get(rule.key), ""));
}
function makeControl(field, row) {
  const label = make("label", field.label.toUpperCase());
  row.append(label);
  if (field.help) row.title = field.help;
  if (field.kind === "toggle") {
    label.className = "label";
    const toggle = button("", () => edit(field.key, isTrue(valueFor(field.key)) ? "False" : "True"));
    toggle.setAttribute("role", "switch");
    toggle.setAttribute("aria-label", field.label);
    row.append(toggle);
    return (value, disabled) => {
      const on = isTrue(value);
      toggle.textContent = on ? "ON" : "OFF";
      toggle.setAttribute("aria-checked", on);
      toggle.disabled = disabled;
    };
  }
  if (field.kind === "choice") {
    const box = make("div", "", "choices");
    label.className = "label";
    row.append(box);
    const choices = field.options.map((option, index) => {
      const value = field.group ? String(index) : option;
      const item = button(option, () => edit(field.key, value));
      box.append(item);
      return { item, value };
    });
    return (selected, disabled) => {
      for (const { item, value } of choices) {
        item.setAttribute("aria-pressed", value === selected);
        item.disabled = disabled;
      }
    };
  }
  if (["editor", "weapons", "ammo", "button"].includes(field.kind)) {
    label.className = "label";
    const item = field.kind === "button"
      ? button(field.label, () => action("addon", field.key))
      : button("EDIT", () => openSettingEditor(field));
    if (field.kind === "button") label.remove();
    item.setAttribute("aria-label", field.kind === "button" ? field.label : "Edit " + field.label);
    row.append(item);
    return (value, disabled) => {
      if (field.kind !== "button") item.title = value;
      item.disabled = disabled;
    };
  }
  const input = make("input");
  input.id = "setting-" + field.key;
  input.autocomplete = "off";
  input.maxLength = field.length;
  if (field.kind === "number") {
    input.inputMode = "decimal";
    input.dataset.number = "";
  }
  label.htmlFor = input.id;
  row.append(input);
  input.addEventListener("input", () => edit(field.key, input.value));
  return (value, disabled) => {
    if (input !== document.activeElement && input.value !== value) input.value = value;
    input.disabled = disabled;
  };
}
function buildControls() {
  const rows = byId("setting-rows");
  rows.replaceChildren();
  byId("connection").replaceChildren();
  controls.clear();
  let group = "";
  for (const field of fields.values()) {
    if (fixedInputs[field.key]) continue;
    const row = make("div", "", field.rules.length ? "row nested" : "row");
    const update = makeControl(field, row);
    let heading;
    if (field.group && field.group !== group) {
      heading = make("h3", field.group.toUpperCase(), "group");
      rows.append(heading);
    }
    group = field.group;
    (field.key === "ConnectionMode" ? byId("connection") : rows).append(row);
    controls.set(field.key, { row, update, heading });
  }
}
function renderControls() {
  if (!state) return;
  const editingLocked = working || state.busy || state.stopping;
  const query = byId("search").value.trim().toLowerCase();
  for (const [key, input] of Object.entries(fixedInputs)) {
    if (input !== document.activeElement && input.value !== valueFor(key)) input.value = valueFor(key);
    input.disabled = editingLocked || fields.get(key).locked;
    input.title = fields.get(key).locked ? "Close the lobby to edit this setting" : "";
  }
  let count = 0;
  let heading;
  for (const [key, control] of controls) {
    const field = fields.get(key);
    if (control.heading) {
      heading = control.heading;
      heading.hidden = true;
    }
    const connection = key === "ConnectionMode";
    control.row.hidden = !connection && !visible(field, query);
    if (!control.row.hidden && !connection) {
      count++;
      if (field.group && heading) heading.hidden = false;
    }
    control.update(valueFor(key), editingLocked || field.locked);
  }
  byId("no-settings").hidden = count > 0;
  byId("apply").textContent = state.hosting ? "APPLY SETTINGS" : "CREATE LOBBY";
  byId("apply").disabled = editingLocked;
  byId("apply").classList.toggle("accent", draft.size > 0);
  byId("close").hidden = !state.hosting;
  byId("close").disabled = editingLocked;
  byId("save").hidden = state.hosting;
  byId("save").disabled = editingLocked || draft.size === 0;
  byId("presets").disabled = editingLocked;
  byId("reset").disabled = editingLocked;
  for (const item of document.querySelectorAll("[data-hosting]")) item.disabled = working || !state.hosting;
  byId("scene").disabled = working || !state.hosting;
  byId("play-scene").disabled = byId("scene").disabled || !byId("scene").value;
  for (const item of byId("players").querySelectorAll("button")) item.disabled = working || item.dataset.unavailable === "true";
}
function renderPlayers() {
  const layout = JSON.stringify([state.canBan, state.players]);
  if (layout === playersLayout) return;
  playersLayout = layout;
  const list = byId("players");
  const focus = list.contains(document.activeElement) ? document.activeElement.dataset.focus : "";
  byId("player-count").textContent = state.players.length;
  list.replaceChildren();
  for (const player of state.players) {
    const row = make("li", "", "player");
    row.append(make("strong", player.name));
    if (player.host || player.isOperator) row.append(make("span", player.host ? "HOST" : "OPERATOR", "tag"));
    row.append(make("span", player.ping < 0 ? "—" : player.ping + " ms", "ping"));
    if (!player.host) {
      const act = name => action(name, player.name, player.id);
      const op = button(player.isOperator ? "Remove operator" : "Make operator", () => act("operator"));
      const kick = button("Kick", () => act("kick"), "danger");
      const ban = button("Ban", () => {
        if (confirm("Ban " + player.name + " for 60 minutes?")) action("ban", player.name);
      }, "danger");
      ban.dataset.unavailable = String(!state.canBan);
      for (const [type, item] of [["operator", op], ["kick", kick], ["ban", ban]]) {
        item.dataset.focus = player.id + ":" + type;
        row.append(item);
      }
    }
    list.append(row);
  }
  if (focus) [...list.querySelectorAll("button")].find(item => item.dataset.focus === focus)?.focus();
}
function renderStatus() {
  status.textContent = problem || state?.status || "Connecting to headless lobby…";
  status.classList.toggle("error", Boolean(problem));
}
function render(snapshot) {
  state = snapshot;
  fields.clear();
  for (const field of state.settings) fields.set(field.key, field);
  const layout = JSON.stringify(state.settings.map(({ value, locked, ...field }) => field));
  if (layout !== fieldsLayout) {
    fieldsLayout = layout;
    buildControls();
  }
  const maps = JSON.stringify(state.maps);
  if (maps !== mapsLayout) {
    mapsLayout = maps;
    fillOptions(byId("maps"), state.maps);
  }
  const scenes = JSON.stringify(state.scenes);
  if (scenes !== sceneLayout) {
    sceneLayout = scenes;
    const select = byId("scene");
    const selected = select.value;
    select.replaceChildren(new Option("Built-in level", ""), ...state.scenes.map(scene =>
      new Option(scene.name === scene.id ? scene.name : scene.name + " (" + scene.id + ")", scene.id)));
    if (state.scenes.some(scene => scene.id === selected)) select.value = selected;
  }
  byId("version").textContent = state.version;
  byId("map-name").textContent = state.map;
  byId("lobby-title").textContent = state.stopping ? "SERVER STOPPING" : state.hosting ? "HOSTED LOBBY" : "NEW LOBBY";
  renderStatus();
  renderPlayers();
  renderControls();
}
async function poll() {
  if (working) return;
  const current = revision;
  try {
    const response = await fetch("/api/state");
    if (!response.ok) throw new Error(await response.text());
    const snapshot = await response.json();
    if (current === revision && !working) render(snapshot);
  } catch {
    if (current === revision && !working) {
      status.textContent = "Waiting for the headless server…";
      status.classList.add("error");
    }
  }
}
async function action(name, value = "", player = 0, submitSettings = false, clearDraft = false) {
  if (working || !state) return;
  working = true;
  revision++;
  problem = "";
  renderStatus();
  renderControls();
  try {
    const response = await fetch("/api/action", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Gunsaw-Admin": "1" },
      body: JSON.stringify({ action: name, value, player, settings: submitSettings ? pending() : [] })
    });
    if (!response.ok) throw new Error(await response.text() || "Request failed.");
    const snapshot = await response.json();
    if (submitSettings || clearDraft) draft.clear();
    render(snapshot);
  } catch (error) {
    problem = error.message;
    renderStatus();
  } finally {
    working = false;
    renderControls();
  }
}
function openDialog(title, commit) {
  if (dialog.open) dialog.close();
  content.replaceChildren();
  byId("dialog-title").textContent = title;
  dialogApply.hidden = !commit;
  dialogCommit = commit;
  dialog.showModal();
}
function splitWeapons(value) {
  return value.split(/;(?![^\[]*\])/);
}
function openSettingEditor(field) {
  const value = valueFor(field.key);
  const inputs = [];
  let read;
  openDialog(field.label.toUpperCase(), () => edit(field.key, read()));
  if (field.help) content.append(make("p", field.help, "editor-note"));
  if (field.kind === "ammo" || field.kind === "weapons") {
    const ammo = field.kind === "ammo";
    const labels = ammo ? ["Pistol", "Rifle", "Heavy", "Grenade"] : ["Slot 1", "Slot 2", "Slot 3"];
    const values = ammo ? value.split(";") : splitWeapons(value);
    for (const [index, label] of labels.entries()) {
      const row = make("label", label, "editor-row");
      const input = make("input");
      input.value = values[index] || (ammo ? "0" : value === "Default" ? "Default" : "None");
      if (ammo) {
        input.type = "number";
        input.min = "0";
        input.max = "999999";
      } else input.setAttribute("list", "weapons");
      inputs.push(input);
      row.append(input);
      content.append(row);
    }
    if (!ammo) loadWeaponOptions();
    read = () => {
      const values = inputs.map(input => input.value.trim());
      if (!ammo && values.every(value => value === "Default")) return "Default";
      return values.map(value => value || (ammo ? "0" : "None")).join(";");
    };
  } else if (field.key === "SawTuner") {
    openTuner(value);
    read = () => tunerValues();
  } else {
    const entries = make("div");
    const add = (entry = "") => {
      const row = make("div", "", "editor-row");
      const input = make("input");
      input.value = entry;
      input.setAttribute("aria-label", field.key === "TeamsCfg" ? "Team name and color" : "Weapon");
      if (field.key === "GGSequence") input.setAttribute("list", "weapons");
      row.append(input, button("Up", () => row.previousElementSibling?.before(row)),
        button("Down", () => row.nextElementSibling?.after(row)), button("Remove", () => row.remove()));
      entries.append(row);
    };
    content.append(entries, button("ADD", () => add()));
    for (const entry of splitWeapons(value).filter(Boolean)) add(entry);
    if (field.key === "GGSequence") loadWeaponOptions();
    read = () => [...entries.querySelectorAll("input")].map(input => input.value.trim()).filter(Boolean).join(";");
  }
}
let weapons;
async function loadWeaponOptions() {
  const list = make("datalist");
  list.id = "weapons";
  content.append(list);
  try {
    if (!weapons) {
      const response = await fetch("/api/weapons");
      if (!response.ok) return;
      weapons = (await response.json()).names;
    }
    fillOptions(list, ["Default", "None", "Random", ...weapons]);
  } catch {}
}
let tunerValues;
function openTuner(value) {
  const overrides = new Map(value.split("\n").filter(Boolean).map(line => line.split("=")));
  tunerValues = () => [...overrides].map(([key, value]) => key + "=" + value).join("\n");
  const species = make("select");
  species.setAttribute("aria-label", "Character");
  const rows = make("div");
  content.append(species, rows);
  fetch("/api/tuner").then(response => {
    if (!response.ok) throw new Error("Could not load characters.");
    return response.json();
  }).then(data => {
    fillOptions(species, data.characters.map(character => character.path));
    const show = () => {
      const character = data.characters.find(character => character.path === species.value);
      rows.replaceChildren();
      if (!character) return;
      for (const [index, stat] of data.stats.entries()) {
        const key = character.path + "|" + index;
        const row = make("label", stat.label, "editor-row");
        const input = make("input");
        input.type = "number";
        input.step = "any";
        input.min = stat.min;
        input.max = stat.max;
        input.placeholder = character.defaults[index];
        input.value = overrides.get(key) ?? "";
        input.addEventListener("input", () => {
          if (input.value) overrides.set(key, input.value);
          else overrides.delete(key);
        });
        row.append(input);
        rows.append(row);
      }
      rows.append(button("RESET CHARACTER", () => {
        for (const [index] of data.stats.entries()) overrides.delete(character.path + "|" + index);
        show();
      }));
    };
    species.addEventListener("change", show);
    show();
  }).catch(error => { rows.textContent = error.message; });
}
function presets() {
  const name = make("input");
  name.maxLength = 32;
  name.placeholder = "Preset name";
  name.setAttribute("aria-label", "Preset name");
  openDialog("PRESETS", null);
  const bar = make("div", "", "bar");
  bar.append(name, button("SAVE", () => {
    if (!name.value.trim()) return name.focus();
    dialog.close();
    action("preset-save", name.value, 0, true);
  }));
  content.append(bar);
  for (const preset of state.presets) {
    const row = make("div", "", "row");
    row.append(make("span", preset), button("LOAD", () => {
      dialog.close();
      action("preset-load", preset, 0, false, true);
    }), button("DELETE", () => {
      if (!confirm("Delete " + preset + "?")) return;
      dialog.close();
      action("preset-delete", preset);
    }, "danger"));
    content.append(row);
  }
}
for (const [key, input] of Object.entries(fixedInputs)) input.addEventListener("input", () => edit(key, input.value));
byId("lobby-form").addEventListener("submit", event => {
  event.preventDefault();
  if (state) action(state.hosting ? "settings" : "create", "", 0, true);
});
byId("save").onclick = () => action("settings", "", 0, true);
byId("close").onclick = () => { if (confirm("Close the lobby and stop the headless server?")) action("close"); };
byId("search").oninput = renderControls;
byId("clear").onclick = () => { byId("search").value = ""; renderControls(); byId("search").focus(); };
byId("presets").onclick = presets;
byId("reset").onclick = () => { if (confirm("Reset lobby settings?")) action("reset", "", 0, false, true); };
byId("scene").onchange = renderControls;
byId("play-scene").onclick = () => action("scene", byId("scene").value);
byId("change").onclick = () => action("change", byId("map").value);
byId("restart").onclick = () => action("restart");
byId("default").onclick = () => action("default");
byId("code").onclick = () => {
  const code = make("textarea");
  code.setAttribute("aria-label", "Custom level code");
  code.placeholder = "Paste a custom level code";
  openDialog("CUSTOM LEVEL", () => action("level", code.value.trim()));
  content.append(code);
  code.focus();
};
byId("dialog-cancel").onclick = () => dialog.close();
dialogApply.onclick = () => { dialog.close(); dialogCommit?.(); };
async function refresh() {
  await poll();
  if (!state?.stopping) setTimeout(refresh, 2000);
}
refresh();
