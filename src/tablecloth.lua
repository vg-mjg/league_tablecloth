-- Lua side adapter that reads the game state and delegates texture composition to native calls:
--   LeagueTablecloth.compose(nicknames, viewerSeat) -> Texture2D
--   LeagueTablecloth.release()
--
-- on match entry it discovers the cloth MeshRenderer, reads the four nicknames + viewer seat,
-- asks compose for the oriented texture, and SetTextures it onto the renderer
-- on a perspective change it re-asks compose for the new seat and re-SetTextures the held materials
-- on match teardown it calls release so our textures don't accumulate across matches

local TAG = "[league_tablecloth]"

local function log(msg)
	MjsLua.log(TAG .. " " .. msg)
end

if type(LeagueTablecloth) ~= "table" or type(LeagueTablecloth.compose) ~= "function" then
	log(
		"LeagueTablecloth.compose missing. Is league_tablecloth.dll installed and loaded after mjslib? Tablecloth disabled."
	)
	return
end

-- the current match's cloth material(s)
local clothMaterials = nil

-- the four players' nicknames in absolute seat order (E/S/W/N = player_datas[1..4]) as a plain array for compose
local function readNicknames()
	local nicks = { "", "", "", "" }
	local desktop = rawget(_G, "DesktopMgr")
	local inst = desktop and desktop.Inst
	local datas = inst and inst.player_datas
	if datas == nil then
		log("DesktopMgr.Inst.player_datas is nil; all seats -> default")
		return nicks
	end
	for seat = 1, 4 do
		local data = datas[seat]
		local nickname = data and data.nickname
		nicks[seat] = (type(nickname) == "string") and nickname or ""
	end
	return nicks
end

-- the viewer's absolute seat (1..4 = E/S/W/N)
-- DesktopMgr.Inst.seat is the local/main seat
-- fall back to 1 if it is unreadable so the cloth still shows in a sensible orientation
local function currentSeat()
	local desktop = rawget(_G, "DesktopMgr")
	local inst = desktop and desktop.Inst
	local seat = inst and inst.seat
	if type(seat) ~= "number" or seat < 1 or seat > 4 then
		return 1
	end
	return seat
end

-- walk every MeshRenderer under the desktop_model
-- logging child/material/texture-property names so the exact cloth renderer stays documented
local function discoverMaterials(model)
	local mats = {}
	if model == nil then
		log("desktop_model is nil -- cannot discover cloth renderer")
		return mats
	end

	local renderers = model.gameObject:GetComponentsInChildren(typeof(UnityEngine.MeshRenderer))
	if renderers == nil then
		log("GetComponentsInChildren(MeshRenderer) returned nil")
		return mats
	end

	local count = renderers.Length
	log("desktop_model has " .. tostring(count) .. " MeshRenderer(s)")
	for idx = 0, count - 1 do
		local r = renderers[idx]
		if r ~= nil then
			local childName = tostring(r.name)
			local mat = r.material
			local matName = (mat ~= nil) and tostring(mat.name) or "<nil>"

			local props = {}
			if mat ~= nil then
				local ok, names = pcall(function()
					return mat:GetTexturePropertyNames()
				end)
				if ok and names ~= nil then
					for p = 0, names.Length - 1 do
						props[#props + 1] = tostring(names[p])
					end
				end
			end

			log(
				string.format(
					"renderer[%d] child=%q material=%q texProps={%s}",
					idx,
					childName,
					matName,
					table.concat(props, ", ")
				)
			)

			-- the "mid" child carries the table centre's own material, not the printed cloth
			if childName ~= "mid" and mat ~= nil then
				mats[#mats + 1] = mat
			end
		end
	end

	return mats
end

-- ask C# for the oriented cloth and SetTexture it onto every held cloth material
local function applyForSeat()
	if clothMaterials == nil or #clothMaterials == 0 then
		return false
	end

	local nicks = readNicknames()
	local seat = currentSeat()
	local ok, tex = pcall(function()
		return LeagueTablecloth.compose(nicks, seat)
	end)
	if not ok or tex == nil then
		log("compose returned no texture (" .. tostring(tex) .. "); leaving vanilla cloth untouched")
		return false
	end

	for _, mat in ipairs(clothMaterials) do
		if mat ~= nil then
			mat:SetTexture("_MainTex", tex)
		end
	end
	log(string.format("applied cloth for seat %d", seat))
	return true
end

-- discover the cloth renderer for this match, then orient+apply for the current seat
local function applyTablecloth()
	local scene = rawget(_G, "Scene_MJ")
	local inst = scene and scene.Inst
	local model = inst and inst.desktop_model

	clothMaterials = discoverMaterials(model)
	if #clothMaterials == 0 then
		log(
			"no cloth renderer found (every MeshRenderer was 'mid' or none existed); "
				.. "leaving vanilla cloth untouched"
		)
		clothMaterials = nil
		return
	end

	if not applyForSeat() then
		log("failed to orient/apply the composed cloth; leaving vanilla cloth untouched")
	end
end

local function safeApply()
	local ok, err = pcall(applyTablecloth)
	if not ok then
		log("apply failed (vanilla cloth left intact): " .. tostring(err))
	end
end

-- where the tablecloth is applied
MjsLua.hook("DesktopMgr.InitRoom", function(orig, self, ...)
	orig(self, ...)
	safeApply()
end)

-- where the perspective changes
MjsLua.hook("DesktopMgr.ChangeMainBody", function(orig, self, ...)
	orig(self, ...)
	local ok, err = pcall(applyForSeat)
	if not ok then
		log("re-orient on ChangeMainBody failed (cloth left as-is): " .. tostring(err))
	end
end)

-- needed to support 'follow dealer' which sets the perspective directly
MjsLua.hook("DesktopMgr.RefreshSeatOnNewRound", function(orig, self, ...)
	orig(self, ...)
	local ok, err = pcall(applyForSeat)
	if not ok then
		log("re-orient on RefreshSeatOnNewRound failed (cloth left as-is): " .. tostring(err))
	end
end)

-- teardown on match end
MjsLua.hook("Scene_MJ._onQuit", function(orig, self, ...)
	clothMaterials = nil
	local ok, err = pcall(function()
		LeagueTablecloth.release()
	end)
	if not ok then
		log("texture cleanup on _onQuit failed (continuing teardown): " .. tostring(err))
	end
	orig(self, ...)
end)

log(
	"installed DesktopMgr.InitRoom, DesktopMgr.ChangeMainBody, "
		.. "DesktopMgr.RefreshSeatOnNewRound, and Scene_MJ._onQuit hooks"
)
