-------------------------------------------
-- lua模块默认的入口
-- @author  zilong
-- @data    2016/09/23
-------------------------------------------

print("hello lua")

local CURRENT_MODULE_NAME = ...

print(CURRENT_MODULE_NAME)

jz = jz or {}
jz.PACKAGE_NAME = string.sub(CURRENT_MODULE_NAME, 1, -6)
jz.VERSION = "0.1.0"

print(jz.PACKAGE_NAME)

require(jz.PACKAGE_NAME .. ".jzLua")
require(jz.PACKAGE_NAME .. ".app")
