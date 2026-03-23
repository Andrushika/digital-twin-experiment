import json, os, pickle

skeleton_list = ['head', 'neck', 'R_shoulder', 'R_Elbow', 'R_hand',
                'L_shoulder', 'L_Elbow', 'L_hand', 'hip', 'R_hip',
                'R_knee', 'R_foot', 'L_hip', 'L_knee', 'L_foot',
                'LeftToeBase', 'RightToeBase',
                'LeftHandThumb1', 'LeftHandThumb2', 'LeftHandThumb3', 
                'LeftHandIndex1', 'LeftHandIndex2', 'LeftHandIndex3', 
                'LeftHandMiddle1', 'LeftHandMiddle2', 'LeftHandMiddle3', 
                'LeftHandRing1', 'LeftHandRing2', 'LeftHandRing3', 
                'LeftHandPinky1', 'LeftHandPinky2', 'LeftHandPinky3', 
                'RightHandThumb1', 'RightHandThumb2', 'RightHandThumb3', 
                'RightHandIndex1', 'RightHandIndex2', 'RightHandIndex3', 
                'RightHandMiddle1', 'RightHandMiddle2', 'RightHandMiddle3', 
                'RightHandRing1', 'RightHandRing2', 'RightHandRing3', 
                'RightHandPinky1', 'RightHandPinky2', 'RightHandPinky3']

json_path = "fbx_data/Violin_0.json"
save_path = "../Motion data/demo.txt"

def read_json(file):
    motion_list = []
    with open(file, 'rb') as jf:
        for line in jf.readlines():
            data = json.loads(line)
            txt_line = ""
            for skeleton in skeleton_list:
                txt_line += str(float(data[f"{skeleton}.x"])) + " " + str(data[f"{skeleton}.y"]) + " " + str(data[f"{skeleton}.z"]) + ","
            txt_line = txt_line[:-1]
            motion_list.append(txt_line)

    return motion_list

def data2txt(save_file, data):
    with open(save_file, "w") as f:
        for coordinate in data:
            f.write(coordinate)
            f.write("\n")

M_List = read_json(json_path)
data2txt(save_path, M_List)

