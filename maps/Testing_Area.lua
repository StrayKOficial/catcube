-- CatCube Testing Area
print("Loading Testing Area...")

local workspace = game:GetService("Workspace")

-- 1. Pillars
for i = 0, 4 do
    local p = Instance.new("Part")
    p.Name = "Pillar_" .. i
    p.Position = Vector3.new(-30 + i*15, 10, -30)
    p.Size = Vector3.new(4, 20, 4)
    p.Color = Color3.new(0.5, 0.5, 0.6)
    p.Anchored = true
    p.Parent = workspace
end

-- 2. Stairs
for i = 0, 10 do
    local step = Instance.new("Part")
    step.Name = "Step_" .. i
    step.Position = Vector3.new(20, i * 1.5, i * 4)
    step.Size = Vector3.new(15, 2, 4)
    step.Color = Color3.new(0.3, 0.3, 0.35)
    step.Anchored = true
    step.Parent = workspace
end

-- 3. High Platform
local plat = Instance.new("Part")
plat.Name = "HighPlatform"
plat.Position = Vector3.new(20, 16, 50)
plat.Size = Vector3.new(20, 2, 20)
plat.Color = Color3.new(0.8, 0.4, 0.2)
plat.Anchored = true
plat.Parent = workspace

print("Map logic executed successfully.")
